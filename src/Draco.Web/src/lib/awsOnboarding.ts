export function getAwsBootstrapErrorMessage(error?: Error | null): string | null {
  const message = error?.message?.trim()
  if (!message) {
    return null
  }

  if (
    message.includes('AWS_ASSUME_ROLE_PRINCIPAL_ARN') ||
    message.includes('principal to trust for cross-account role access')
  ) {
    return 'Guided AWS onboarding is not configured for this Draco workspace yet. Open the AWS Setup Guide for the exact admin and user steps, or use Access Keys under Advanced.'
  }

  return message
}

export function copyToClipboard(value: string): Promise<void> {
  return navigator.clipboard.writeText(value)
}
