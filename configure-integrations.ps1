param()
$ErrorActionPreference = 'Stop'
$local = Join-Path $PSScriptRoot '.local'
$target = Join-Path $local 'integrations.json'
if (Test-Path -LiteralPath $target) { throw 'Configuration already exists at .local/integrations.json. Edit that file locally; this script will not overwrite secrets.' }
Write-Host 'QueDrink integration setup. Secrets stay on this computer and are not printed.'
$site = (Read-Host 'Public website origin, or Enter for http://127.0.0.1:3006').Trim()
if (!$site) { $site = 'http://127.0.0.1:3006' }
$siteUri = $null
if (![Uri]::TryCreate($site, [UriKind]::Absolute, [ref]$siteUri) -or $siteUri.Scheme -notin @('http','https') -or $siteUri.AbsolutePath -ne '/' -or $siteUri.Query -or $siteUri.Fragment -or $siteUri.UserInfo) { throw 'Enter only a website origin, for example https://your-domain.vn.' }
$gmailAddress = (Read-Host 'Gmail sender address, or Enter to leave email disabled').Trim()
$gmailPassword = ''
if ($gmailAddress) {
    $mailAddress = New-Object System.Net.Mail.MailAddress($gmailAddress)
    if ($mailAddress.Address -ne $gmailAddress) { throw 'Enter only the email address, without a display name.' }
    $secureGmail = Read-Host 'Google App Password (hidden; not your Google account password)' -AsSecureString
    $gmailPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureGmail)
    try { $gmailPassword = ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($gmailPointer)) -replace '\s','' }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($gmailPointer) }
    if ($gmailPassword.Length -ne 16) { throw 'Google App Password must have 16 characters after removing spaces.' }
}
Write-Host 'Payment: 0 = disabled, 1 = VNPay sandbox, 2 = VNPay live (approved merchant required).'
$mode = Read-Host 'Choose payment mode [0]'
if (!$mode) { $mode = '0' }
if ($mode -notin @('0','1','2')) { throw 'Choose 0, 1 or 2.' }
$merchant = ''; $paymentSecret = ''
if ($mode -ne '0') {
    $merchant = (Read-Host 'VNPAY TmnCode from your merchant account').Trim()
    if ($merchant -notmatch '^[A-Za-z0-9]{4,32}$') { throw 'Invalid merchant code.' }
    $securePayment = Read-Host 'VNPAY HashSecret (hidden)' -AsSecureString
    $paymentPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePayment)
    try { $paymentSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($paymentPointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($paymentPointer) }
    if ($paymentSecret.Length -lt 16) { throw 'Invalid VNPAY HashSecret.' }
}
if ($mode -eq '2' -and ($siteUri.Scheme -ne 'https' -or $siteUri.IsLoopback)) { throw 'Live payments require a public HTTPS website and a registered IPN URL.' }
New-Item -ItemType Directory -Force -Path $local | Out-Null
$settings = @{ FRONTEND_URL = $site.TrimEnd('/'); GMAIL_ENABLED = [bool]$gmailAddress; GMAIL_ADDRESS = $gmailAddress; GMAIL_APP_PASSWORD = $gmailPassword;
    PAYMENT_PROVIDER = $(if ($mode -eq '0') { 'DISABLED' } else { 'VNPAY' }); VNPAY_SANDBOX = $mode -ne '2'; VNPAY_TMN_CODE = $merchant; VNPAY_HASH_SECRET = $paymentSecret }
try {
    $settings | ConvertTo-Json | Set-Content -LiteralPath $target -Encoding UTF8
    $permissions = Get-Acl -LiteralPath $target
    $permissions.SetAccessRuleProtection($true, $false)
    $userName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $permissions.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($userName, 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $target -AclObject $permissions
} finally { $gmailPassword = $null; $paymentSecret = $null; $settings = $null; $secureGmail = $null; $securePayment = $null }
Write-Host 'Saved .local/integrations.json with access limited to your Windows account. Do not commit or share this file.'
Write-Host 'Restart: .\start-demo.ps1 -Stop, then .\start-demo.ps1. Test Gmail from the admin dashboard.'
if ($mode -ne '0') {
    if ($siteUri.Scheme -ne 'https' -or $siteUri.IsLoopback) { Write-Host 'Local URL signing is available; gateway IPN tests still require a public HTTPS website.' }
    else { Write-Host "Register this IPN with VNPay: $($site.TrimEnd('/'))/api/v1/payments/vnpay/ipn" }
}
