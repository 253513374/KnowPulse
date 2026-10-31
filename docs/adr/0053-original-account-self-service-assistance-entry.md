# ADR 0053: Original-account self-service assistance entry

Status: Accepted, 2026-10-18, for the existing self-service entry portion of FR01/FR35. The user subsequently confirmed D06/D10 account-support responsibility: owners verify the original account for self-service; exceptions require platform manual review. Schools do not take over the account. The exceptional application and controlled execution workflow is not yet implemented.

## Existing abilities, one discoverable entry

Add anonymous Web route /account/assistance, linked from the home page, teaching workspace and private WeChat binding page. Native login and both workspaces offer a user-initiated copy of that fixed public portal address, including before WeChat configuration/login. The address contains no user, tenant, password, reset proof or binding code; the existing native destination allowlist rejects query/fragment/foreign destinations.

The page distinguishes original-account password recovery, owner binding management, school identity checks and cases unable to establish original ownership. Recovery of an account is not a new account, school membership or asset transfer. It explains the existing server timer and server-confirmed-answer boundaries without claiming unsynchronized content survives a device change. Current-account display follows authentication state; it is not an authority grant.

Use the current configured authentication server's built-in MVC password recovery page. ABP 10.6.0's [ForgotPassword page](https://raw.githubusercontent.com/abpframework/abp/10.6.0/modules/account/src/Volo.Abp.Account.Web/Pages/Account/ForgotPassword.cshtml) and [handler](https://raw.githubusercontent.com/abpframework/abp/10.6.0/modules/account/src/Volo.Abp.Account.Web/Pages/Account/ForgotPassword.cshtml.cs) bind an email and request the MVC reset flow; the existing Host already registers Account Web/OpenIddict and its MVC root. Preserve a configured authority path base. Only HTTPS or loopback HTTP is accepted, with no configured credentials/query/fragment. A malformed authority exposes no recovery/login action. Do not implement another email/token form or accept a client-selected account/return URL.

Ordinary login uses the existing WASM RemoteAuthenticationOptions login path and NavigateToLogin, preserving the normal OIDC handshake and return flow. It does not navigate directly to the API's standalone login page and assume the SPA is signed in. Owner binding navigation continues to the original authorized page; its current account checks, issuance password verification, revocation and private history remain unchanged. School managers gain no access to another account's binding, credentials, personal library or records.

## Configuration and verification boundary

There is no schema, credential, membership or recovery-policy change. Real verification email delivery is not established by a successful navigation: this repo replaces IEmailSender with NullEmailSender in DEBUG. No email/password-reset request was sent in this slice. Actual mail transport, browser flow, tenant selection, real original-account recovery, WeChat/SDK/device and unsynchronized exam behavior require subsequent acceptance.

The user selected original-account self-verification with platform review for exceptions on 2026-10-18. Record this as a confirmed responsibility decision, not as completed implementation or real ownership verification. Keep the full FR01/FR35 objective open; do not call a guide or built-in capability a completed assisted-recovery/unbinding request workflow. Other retention, school-exit access and student-support policies under D06/D10 remain unresolved.

Validation is the solution build, five direct URI-boundary checks, one selected native public-path check and focused static entry/localization/handler checks. No full regression, live API/browser, external sender or new dependency is introduced.
