// AqtCoreFW — Jenkins Shared Library: govPipeline
// Reusable pipeline definition for GOV .NET 10 backend services.
// Usage in Jenkinsfile: @Library('gov-shared-lib') _; govPipeline(appName: 'aqtcorefw', ...)

/**
 * Main entry point — call() allows the var to be invoked as a step.
 *
 * @param config Map with keys:
 *   appName    (required) — application identifier, e.g. 'aqtcorefw'
 *   registry   (required) — container registry, e.g. 'nexus.internal:5000'
 *   chartPath  (required) — path to Helm chart, e.g. 'infra/helm/aqtcorefw'
 *   solution   (optional) — path to .slnx file; defaults to 'src/AqtCoreFW.slnx'
 *   emailTo    (optional) — failure notification recipient
 */
def call(Map config) {
    config.solution  = config.get('solution',  'src/AqtCoreFW.slnx')
    config.emailTo   = config.get('emailTo',   'devops@aequitas.internal')
    config.dotnetImg = 'mcr.microsoft.com/dotnet/sdk:10.0-alpine'

    pipeline {
        agent none

        options {
            timeout(time: 15, unit: 'MINUTES')
            buildDiscarder(logRotator(numToKeepStr: '20'))
            disableConcurrentBuilds()
            timestamps()
        }

        stages {
            stage('SAST')     { steps { runSast(config) } }
            stage('Build')    { steps { buildPublish(config) } }
            stage('Test')     {
                steps { runTests(config) }
                post  { always { junit 'TestResults/**/*.xml' } }
            }
            stage('Security') { steps { runSecurity(config) } }
            stage('Package')  { steps { packageAndSign(config) } }
            stage('Deploy-Staging') {
                when { anyOf { branch 'main'; branch 'develop' } }
                steps { deployHelm(config, 'staging', 'staging') }
            }
            stage('Deploy-Prod') {
                when  { branch 'main' }
                steps {
                    timeout(time: 30, unit: 'MINUTES') {
                        input message: "Deploy ${config.appName} #${env.BUILD_NUMBER} to PRODUCTION?",
                              ok: 'Approve',
                              submitter: 'release-managers'
                    }
                    deployHelm(config, 'prod', 'prod')
                }
            }
        }

        post {
            failure { govNotify.failure("${config.appName} pipeline #${env.BUILD_NUMBER} failed.", config.emailTo) }
            success { govNotify.success("${config.appName} pipeline #${env.BUILD_NUMBER} succeeded.") }
        }
    }
}

// ─── Stage implementations ────────────────────────────────────────────────────

/**
 * runSast — Roslyn static analysis + SCA vulnerable packages list + SonarQube.
 */
def runSast(Map config) {
    node {
        docker.image(config.dotnetImg).inside {
            checkout scm
            sh "mkdir -p reports/sast"

            sh """
                dotnet build ${config.solution} -c Release \
                    /p:RunAnalyzers=true \
                    /p:AnalysisMode=All \
                    /flp:logfile=reports/sast/build-analysis.log;verbosity=diagnostic
            """

            sh """
                dotnet list ${config.solution} package \
                    --vulnerable --include-transitive \
                    > reports/sast/vulnerable-packages.txt 2>&1 || true
            """

            withSonarQubeEnv('SonarQube') {
                sh """
                    dotnet tool install --global dotnet-sonarscanner || true
                    dotnet sonarscanner begin /k:"${config.appName}"
                    dotnet build ${config.solution} -c Release
                    dotnet sonarscanner end
                """
            }

            archiveArtifacts artifacts: 'reports/sast/**', allowEmptyArchive: true
        }
    }
}

/**
 * buildPublish — dotnet publish both Api and AuthServer to publish/ directories.
 */
def buildPublish(Map config) {
    node {
        docker.image(config.dotnetImg).inside {
            checkout scm
            sh "mkdir -p publish/api publish/authserver"

            sh """
                dotnet publish src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj \
                    -c Release -o publish/api --no-restore /p:UseAppHost=false
                dotnet publish src/host/AqtCoreFW.AuthServer/AqtCoreFW.AuthServer.csproj \
                    -c Release -o publish/authserver --no-restore /p:UseAppHost=false
            """

            archiveArtifacts artifacts: 'publish/**', fingerprint: true
        }
    }
}

/**
 * runTests — xUnit with XPlat Code Coverage. Fails pipeline on test failure.
 */
def runTests(Map config) {
    node {
        docker.image(config.dotnetImg).inside {
            checkout scm
            sh "mkdir -p TestResults"

            sh """
                dotnet test ${config.solution} \
                    --collect:"XPlat Code Coverage" \
                    --results-directory TestResults/ \
                    --logger "junit;LogFilePath=TestResults/junit-{assembly}.xml" \
                    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
            """
        }
    }
}

/**
 * runSecurity — OWASP ZAP DAST + Trivy filesystem scan + CycloneDX SBOM.
 */
def runSecurity(Map config) {
    node('docker-host') {
        checkout scm
        sh "mkdir -p reports/security"

        sh """
            docker run --rm \
                -v \${PWD}/reports/security:/zap/wrk:rw \
                owasp/zap2docker-stable \
                zap-baseline.py -t https://staging.internal \
                    -J /zap/wrk/zap-report.json -I || true
        """

        sh """
            docker run --rm -v \${PWD}:/workspace \
                aquasecurity/trivy:latest fs \
                    --security-checks vuln,secret \
                    --exit-code 1 --severity HIGH,CRITICAL \
                    --format json \
                    --output /workspace/reports/security/trivy-fs-report.json \
                    /workspace/src || true
        """

        sh "dotnet tool install --global CycloneDX || true"
        sh "dotnet CycloneDX ${config.solution} --json --out reports/security/ || true"

        archiveArtifacts artifacts: 'reports/security/**', allowEmptyArchive: true
    }
}

/**
 * packageAndSign — Kaniko image build + push to Nexus + Cosign signature.
 */
def packageAndSign(Map config) {
    node('kaniko-agent') {
        checkout scm

        withCredentials([
            usernamePassword(credentialsId: 'nexus-registry', usernameVariable: 'REG_USER', passwordVariable: 'REG_PASS'),
            file(credentialsId: 'cosign-private-key', variable: 'COSIGN_KEY')
        ]) {
            sh """
                mkdir -p /kaniko/.docker
                echo '{"auths":{"${config.registry}":{"auth":"'"\$(echo -n '\$REG_USER:\$REG_PASS' | base64)"'"}}}' \
                    > /kaniko/.docker/config.json
            """

            sh """
                /kaniko/executor \
                    --context \${PWD} \
                    --dockerfile src/host/AqtCoreFW.Api/Dockerfile \
                    --destination ${config.registry}/${config.appName}/api:${env.BUILD_NUMBER} \
                    --destination ${config.registry}/${config.appName}/api:latest \
                    --cache=true --snapshot-mode=redo
            """

            sh """
                cosign sign --key \$COSIGN_KEY \
                    ${config.registry}/${config.appName}/api:${env.BUILD_NUMBER}
            """
        }
    }
}

/**
 * deployHelm — Helm upgrade/install to the target namespace.
 *
 * @param config   pipeline config map
 * @param envName  values file suffix, e.g. 'staging' → values-staging.yaml
 * @param namespace  K8s namespace to deploy into
 */
def deployHelm(Map config, String envName, String namespace) {
    node('helm-agent') {
        checkout scm

        sh """
            helm upgrade --install \
                ${config.appName}-${envName} \
                ${config.chartPath}/ \
                --namespace ${namespace} \
                --create-namespace \
                --values ${config.chartPath}/values-${envName}.yaml \
                --set image.tag=${env.BUILD_NUMBER} \
                --atomic --timeout 5m --wait
        """
    }
}
