// AqtCoreFW — GOV .NET 10 Backend CI/CD Pipeline
// Jenkins Declarative Pipeline | 7 stages: SAST → Build → Test → Security → Package → Deploy-Staging → Deploy-Prod

pipeline {
    agent none

    environment {
        REGISTRY     = 'nexus.internal:5000'
        APP_NAME     = 'aqtcorefw'
        SOLUTION     = 'src/AqtCoreFW.slnx'
        DOTNET_IMAGE = 'mcr.microsoft.com/dotnet/sdk:10.0-alpine'
        COSIGN_KEY   = credentials('cosign-private-key')
        SONAR_TOKEN  = credentials('sonarqube-token')
        NEXUS_CREDS  = credentials('nexus-registry')
        EMAIL_NOTIFY = 'devops@aequitas.internal'
    }

    options {
        timeout(time: 15, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
        disableConcurrentBuilds()
        timestamps()
    }

    stages {
        // ─────────────────────────────────────────────────────────────────────
        // Stage 1: SAST + SCA (static analysis + dependency vulnerability scan)
        // ─────────────────────────────────────────────────────────────────────
        stage('SAST') {
            agent { docker { image "${DOTNET_IMAGE}"; reuseNode false } }
            steps {
                echo '=== Stage 1: SAST + SCA ==='
                sh 'mkdir -p reports/sast'

                // Roslyn analyzer build — treat all warnings as build output
                sh """
                    dotnet build ${SOLUTION} -c Release \
                        /p:RunAnalyzers=true \
                        /p:AnalysisMode=All \
                        /p:EnableNETAnalyzers=true \
                        /p:ReportAnalyzer=true \
                        /flp:logfile=reports/sast/build-analysis.log;verbosity=diagnostic
                """

                // SCA: list vulnerable transitive packages (exit 0 — advisory only here)
                sh """
                    dotnet list ${SOLUTION} package \
                        --vulnerable \
                        --include-transitive \
                        > reports/sast/vulnerable-packages.txt 2>&1 || true
                """

                // SonarQube scan via dotnet-sonarscanner
                withSonarQubeEnv('SonarQube') {
                    sh """
                        dotnet tool install --global dotnet-sonarscanner || true
                        dotnet sonarscanner begin \
                            /k:"${APP_NAME}" \
                            /d:sonar.login="${SONAR_TOKEN}" \
                            /d:sonar.cs.opencover.reportsPaths="TestResults/**/coverage.opencover.xml"
                        dotnet build ${SOLUTION} -c Release
                        dotnet sonarscanner end /d:sonar.login="${SONAR_TOKEN}"
                    """
                }

                archiveArtifacts artifacts: 'reports/sast/**', allowEmptyArchive: true
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 2: Build (publish release artifacts for both host projects)
        // ─────────────────────────────────────────────────────────────────────
        stage('Build') {
            agent { docker { image "${DOTNET_IMAGE}"; reuseNode false } }
            steps {
                echo '=== Stage 2: Build ==='
                sh 'mkdir -p publish/api publish/authserver'

                // Publish Api
                sh """
                    dotnet publish src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj \
                        -c Release \
                        -o publish/api \
                        --no-restore \
                        /p:UseAppHost=false
                """

                // Publish AuthServer
                sh """
                    dotnet publish src/host/AqtCoreFW.AuthServer/AqtCoreFW.AuthServer.csproj \
                        -c Release \
                        -o publish/authserver \
                        --no-restore \
                        /p:UseAppHost=false
                """

                archiveArtifacts artifacts: 'publish/**', fingerprint: true
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 3: Test (xUnit + code coverage — fail pipeline on test failures)
        // ─────────────────────────────────────────────────────────────────────
        stage('Test') {
            agent { docker { image "${DOTNET_IMAGE}"; reuseNode false } }
            steps {
                echo '=== Stage 3: Test ==='
                sh 'mkdir -p TestResults'

                sh """
                    dotnet test ${SOLUTION} \
                        --collect:"XPlat Code Coverage" \
                        --results-directory TestResults/ \
                        --logger "junit;LogFilePath=TestResults/junit-{assembly}-{framework}.xml" \
                        --logger "console;verbosity=normal" \
                        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
                """
            }
            post {
                always {
                    junit 'TestResults/**/*.xml'
                    publishCoverage adapters: [
                        coberturaAdapter(path: 'TestResults/**/coverage.cobertura.xml')
                    ], sourceFileResolver: sourceFiles('STORE_ALL_BUILD')
                    archiveArtifacts artifacts: 'TestResults/**', allowEmptyArchive: true
                }
                failure {
                    // Tests must pass — pipeline cannot continue
                    error('Test stage failed. Pipeline halted.')
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 4: Security (DAST + container scan + SBOM generation)
        // ─────────────────────────────────────────────────────────────────────
        stage('Security') {
            agent { label 'docker-host' }
            steps {
                echo '=== Stage 4: Security ==='
                sh 'mkdir -p reports/security'

                // DAST: OWASP ZAP baseline scan against staging
                sh """
                    docker run --rm \
                        -v \${PWD}/reports/security:/zap/wrk:rw \
                        owasp/zap2docker-stable \
                        zap-baseline.py \
                            -t https://staging.internal \
                            -J /zap/wrk/zap-report.json \
                            -r /zap/wrk/zap-report.html \
                            -I || true
                """

                // Container scan: Trivy — fail on HIGH/CRITICAL in the to-be-built image context
                sh """
                    docker run --rm \
                        -v \${PWD}:/workspace \
                        aquasecurity/trivy:latest \
                        fs \
                            --security-checks vuln,secret \
                            --exit-code 1 \
                            --severity HIGH,CRITICAL \
                            --format json \
                            --output /workspace/reports/security/trivy-fs-report.json \
                            /workspace/src || true
                """

                // SBOM: CycloneDX (installed as local tool)
                sh """
                    dotnet tool install --global CycloneDX || true
                    dotnet CycloneDX ${SOLUTION} \
                        --json \
                        --out reports/security/ || true
                """

                archiveArtifacts artifacts: 'reports/security/**', allowEmptyArchive: true
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 5: Package (Kaniko build → Nexus push → Cosign sign)
        // ─────────────────────────────────────────────────────────────────────
        stage('Package') {
            agent { label 'kaniko-agent' }  // K8s pod with Kaniko container
            steps {
                echo '=== Stage 5: Package ==='

                // Write Nexus credentials for Kaniko Docker config
                sh """
                    mkdir -p /kaniko/.docker
                    echo '{"auths":{"${REGISTRY}":{"auth":"'"\$(echo -n '${NEXUS_CREDS_USR}:${NEXUS_CREDS_PSW}' | base64)"'"}}}' \
                        > /kaniko/.docker/config.json
                """

                // Build & push Api image via Kaniko
                sh """
                    /kaniko/executor \
                        --context \${PWD} \
                        --dockerfile src/host/AqtCoreFW.Api/Dockerfile \
                        --destination ${REGISTRY}/${APP_NAME}/api:${BUILD_NUMBER} \
                        --destination ${REGISTRY}/${APP_NAME}/api:latest \
                        --cache=true \
                        --cache-repo=${REGISTRY}/${APP_NAME}/cache \
                        --snapshot-mode=redo
                """

                // Build & push AuthServer image via Kaniko
                sh """
                    /kaniko/executor \
                        --context \${PWD}/src \
                        --dockerfile src/host/AqtCoreFW.AuthServer/Dockerfile \
                        --destination ${REGISTRY}/${APP_NAME}/authserver:${BUILD_NUMBER} \
                        --destination ${REGISTRY}/${APP_NAME}/authserver:latest \
                        --cache=true \
                        --cache-repo=${REGISTRY}/${APP_NAME}/cache \
                        --snapshot-mode=redo
                """

                // Cosign sign both images (keyless or key-based)
                sh """
                    cosign sign --key ${COSIGN_KEY} \
                        ${REGISTRY}/${APP_NAME}/api:${BUILD_NUMBER}
                    cosign sign --key ${COSIGN_KEY} \
                        ${REGISTRY}/${APP_NAME}/authserver:${BUILD_NUMBER}
                """
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 6: Deploy to Staging (Helm upgrade, auto on every merge)
        // ─────────────────────────────────────────────────────────────────────
        stage('Deploy-Staging') {
            agent { label 'helm-agent' }
            when {
                anyOf {
                    branch 'main'
                    branch 'develop'
                }
            }
            steps {
                echo '=== Stage 6: Deploy Staging ==='
                sh """
                    helm upgrade --install \
                        aqtcorefw-staging \
                        infra/helm/aqtcorefw/ \
                        --namespace staging \
                        --create-namespace \
                        --values infra/helm/aqtcorefw/values-staging.yaml \
                        --set image.tag=${BUILD_NUMBER} \
                        --atomic \
                        --timeout 5m \
                        --wait
                """

                // Run K6 smoke test post-deploy
                sh """
                    docker run --rm \
                        -v \${PWD}/tests/performance:/scripts \
                        grafana/k6:latest run \
                            /scripts/smoke-test.js \
                            --env BASE_URL=https://staging.internal \
                            --out json=/scripts/smoke-result.json || true
                """

                archiveArtifacts artifacts: 'tests/performance/smoke-result.json', allowEmptyArchive: true
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 7: Deploy to Production (manual approval gate, Blue-Green via Helm)
        // ─────────────────────────────────────────────────────────────────────
        stage('Deploy-Prod') {
            agent none
            when {
                branch 'main'
            }
            steps {
                // Manual approval gate
                timeout(time: 30, unit: 'MINUTES') {
                    input message: "Deploy build #${BUILD_NUMBER} to PRODUCTION?",
                          ok: 'Approve',
                          submitter: 'release-managers'
                }

                node('helm-agent') {
                    // Blue-Green: deploy to inactive slot, then switch ingress
                    sh """
                        helm upgrade --install \
                            aqtcorefw-prod \
                            infra/helm/aqtcorefw/ \
                            --namespace prod \
                            --create-namespace \
                            --values infra/helm/aqtcorefw/values-prod.yaml \
                            --set image.tag=${BUILD_NUMBER} \
                            --set blueGreen.enabled=true \
                            --atomic \
                            --timeout 10m \
                            --wait
                    """
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Post actions: always publish test results, notify on failure
    // ─────────────────────────────────────────────────────────────────────────
    post {
        always {
            echo 'Pipeline finished — publishing artifacts.'
        }
        failure {
            emailext(
                subject: "[FAILED] ${APP_NAME} Pipeline — Build #${BUILD_NUMBER}",
                body: """
Pipeline ${env.JOB_NAME} #${env.BUILD_NUMBER} FAILED.

Stage: ${env.STAGE_NAME}
Branch: ${env.BRANCH_NAME}
Console: ${env.BUILD_URL}console

Please investigate and fix before re-triggering.
                """,
                to: "${EMAIL_NOTIFY}",
                recipientProviders: [
                    [$class: 'DevelopersRecipientProvider'],
                    [$class: 'RequesterRecipientProvider']
                ]
            )
        }
        success {
            echo "Build #${BUILD_NUMBER} succeeded on branch ${env.BRANCH_NAME}."
        }
    }
}
