#requires -Version 7.0
[CmdletBinding()]
param(
    [uri]$BaseUrl = 'https://localhost:44305',
    [ValidateRange(1, 60)]
    [int]$TimeoutSec = 15
)

$ErrorActionPreference = 'Stop'
if (-not $BaseUrl.IsAbsoluteUri -or -not $BaseUrl.IsLoopback -or
    $BaseUrl.Scheme -ne 'https' -or $BaseUrl.Port -ne 44305 -or
    $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment -or $BaseUrl.AbsolutePath -ne '/') {
    throw 'This check only permits a loopback HTTPS origin on port 44305.'
}

$checks = [System.Collections.Generic.List[object]]::new()
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$loginUrl = [uri]::new($BaseUrl, 'Account/Login')
$fakeUser = 'missing-brand-login-' + [guid]::NewGuid().ToString('N')
$fakePassword = 'NotARealAccount!' + [guid]::NewGuid().ToString('N')

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}

function Get-InputValue {
    param([string]$Html, [string]$Name)
    $tag = [regex]::Match($Html, '<input\b[^>]*\bname="' + [regex]::Escape($Name) + '"[^>]*>', 'IgnoreCase').Value
    return [System.Net.WebUtility]::HtmlDecode([regex]::Match($tag, '\bvalue="([^"]*)"', 'IgnoreCase').Groups[1].Value)
}

function Get-Feedback {
    param([string]$Html)
    $content = [regex]::Match($Html, '<div\b[^>]*\bid="kp-login-feedback"[^>]*>(.*?)</div>', 'IgnoreCase, Singleline').Groups[1].Value
    return [System.Net.WebUtility]::HtmlDecode([regex]::Replace($content, '<[^>]+>', '')).Trim()
}

function Invoke-LoginRequest {
    param([uri]$Uri, [string]$Method = 'GET', [hashtable]$Body, [hashtable]$Headers = @{})
    $arguments = @{
        Uri = $Uri
        Method = $Method
        WebSession = $session
        Headers = $Headers
        TimeoutSec = $TimeoutSec
        SkipCertificateCheck = $true
        SkipHttpErrorCheck = $true
        MaximumRedirection = 0
    }
    if ($null -ne $Body) { $arguments.Body = $Body }
    return Invoke-WebRequest @arguments
}

try {
    $page = Invoke-LoginRequest -Uri $loginUrl
    $html = [string]$page.Content
    $formPresent = $page.StatusCode -eq 200 -and $html -match '\bid="kp-login-form"'
    Add-Check 'Brand login form is available' $formPresent "HTTP $($page.StatusCode)"
    if (-not $formPresent) { throw 'The expected login form was not returned.' }

    foreach ($name in @('LoginInput.UserNameOrEmailAddress', 'LoginInput.Password', 'LoginInput.RememberMe', 'ReturnUrl', 'ReturnUrlHash')) {
        Add-Check "Form field: $name" ($html -match ('\bname="' + [regex]::Escape($name) + '"')) 'Existing account/OIDC field'
    }
    $token = Get-InputValue $html '__RequestVerificationToken'
    Add-Check 'Antiforgery token is rendered' (-not [string]::IsNullOrWhiteSpace($token)) 'Token value is not logged'
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'The antiforgery token is missing.' }

    foreach ($path in @('css/brand-login.css', 'js/brand-login.js', 'images/brand-login/brand-mark.svg',
        'images/brand-login/book.svg', 'images/brand-login/ai.svg', 'images/brand-login/exam.svg')) {
        $asset = Invoke-LoginRequest -Uri ([uri]::new($BaseUrl, $path))
        Add-Check "Static asset: $path" ($asset.StatusCode -eq 200 -and $asset.Content.Length -gt 0) "HTTP $($asset.StatusCode)"
    }

    $loginBody = @{
        Action = 'Login'
        'LoginInput.UserNameOrEmailAddress' = $fakeUser
        'LoginInput.Password' = $fakePassword
        'LoginInput.RememberMe' = 'false'
        ReturnUrl = Get-InputValue $html 'ReturnUrl'
        ReturnUrlHash = Get-InputValue $html 'ReturnUrlHash'
    }
    $withoutToken = Invoke-LoginRequest -Uri $loginUrl -Method POST -Body $loginBody
    Add-Check 'POST without antiforgery is rejected' ($withoutToken.StatusCode -in @(400, 403)) "HTTP $($withoutToken.StatusCode)"

    $loginBody['__RequestVerificationToken'] = $token
    foreach ($mode in @('Native', 'AJAX')) {
        $headers = if ($mode -eq 'AJAX') {
            @{ 'X-Requested-With' = 'XMLHttpRequest'; Accept = 'application/json, text/html' }
        } else { @{ Accept = 'text/html' } }
        $response = Invoke-LoginRequest -Uri $loginUrl -Method POST -Body $loginBody -Headers $headers
        $body = [string]$response.Content
        $isHtml = "$($response.Headers['Content-Type'])" -like 'text/html*'
        $feedback = Get-Feedback $body
        Add-Check "$mode failure remains HTML" ($response.StatusCode -eq 200 -and $isHtml -and $body -match '\bid="kp-login-form"') "HTTP $($response.StatusCode); no fabricated JSON success"
        Add-Check "$mode failure contains feedback" (-not [string]::IsNullOrWhiteSpace($feedback)) 'Server-rendered account feedback is present'
        Add-Check "$mode feedback does not echo the username" (-not $feedback.Contains($fakeUser)) 'The synthetic username is absent from the error'
        Add-Check "$mode response does not echo the password" (-not $body.Contains($fakePassword)) 'Synthetic password is absent from the response'
    }
} catch {
    Add-Check 'HTTP verification completed' $false $_.Exception.Message
}

$checks | Format-Table -AutoSize
if ($checks | Where-Object { -not $_.Passed }) { exit 1 }
