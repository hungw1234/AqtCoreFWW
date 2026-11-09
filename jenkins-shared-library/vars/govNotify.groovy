// AqtCoreFW — Jenkins Shared Library: govNotify
// Centralised notification helpers for GOV pipeline stages.
// Usage: govNotify.failure("message", "email@example.com")
//        govNotify.success("message")

/**
 * failure — send EmailExt notification and print to console.
 *
 * @param message  human-readable failure description
 * @param emailTo  recipient address (defaults to devops@aequitas.internal)
 */
def failure(String message, String emailTo = 'devops@aequitas.internal') {
    echo "[GOV-NOTIFY] FAILURE: ${message}"

    emailext(
        subject: "[FAILED] ${env.JOB_NAME} — Build #${env.BUILD_NUMBER}",
        body: """
Pipeline failure detected.

Message : ${message}
Job     : ${env.JOB_NAME}
Build   : #${env.BUILD_NUMBER}
Branch  : ${env.BRANCH_NAME ?: 'unknown'}
Console : ${env.BUILD_URL}console

Please investigate immediately.
        """.stripIndent(),
        to: emailTo,
        recipientProviders: [
            [$class: 'DevelopersRecipientProvider'],
            [$class: 'RequesterRecipientProvider']
        ],
        mimeType: 'text/plain'
    )
}

/**
 * success — log success to console only (no email on green builds).
 *
 * @param message  human-readable success description
 */
def success(String message) {
    echo "[GOV-NOTIFY] SUCCESS: ${message}"
}
