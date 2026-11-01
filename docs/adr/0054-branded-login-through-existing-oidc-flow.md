# ADR 0054: Brand home and login share the existing OIDC entry

Status: Accepted, 2026-10-31, following the approved brand/login Figma and responsive constraints.

2026-11-01: [ADR 0055](0055-retire-school-workflows.md) retires school workflows. The no-feature fallback below is now `/workspace`; the brand login and teacher shell decisions remain in effect.

## Entry and authority

The Web root is the authenticated workspace. Anonymous visits use the existing `AuthorizeRouteView` and `WebAssemblyRedirectToLogin`, preserving the configured OIDC code/PKCE flow. The authentication server's `/Account/Login` presents the public brand story and real login form together. Authenticated workspace permissions are unchanged; `/account/assistance` remains anonymous and is linked from the login page, continuing ADR 0053.

After authentication, `/` selects the first existing teacher feature allowed by the current permissions, starting with `/teacher/materials`. Users who only have management permissions enter `/admin`; other authenticated users retain `/education/workspace`. This replaces the obsolete five-card landing page, rather than changing an OIDC return URL. Explicit deep links still return to their original destinations.

Teacher routes use the Figma teacher navigation and header through `TeacherLayout`, nested in the existing `MainLayout` so ABP authentication, dialogs and notifications remain available. Its five primary links are filtered by their existing permissions. Account help, school/teaching tools, material Q&A and the separately authorized management workspace remain available from the account menu. Route authorization and backend data boundaries remain authoritative.

The Razor page inherits `OpenIddictSupportedLoginModel`. Credential checking, lockout, tenant hints, two-factor routing and return URL validation remain with ABP. The form preserves the original field names, antiforgery token, return values, external providers, cancellation and registration/local-login settings. It does not add a password API, grant a school identity or treat an authentication-server cookie as a signed-in WebAssembly client.

The fixed public assistance link uses `App:ClientUrl`, preserving a configured path base and rejecting credential/query/fragment-bearing or insecure remote URLs. Missing or malformed configuration omits that action without preventing password login. Deployments must configure their actual public Web root. ABP's current open-source two-factor handler is unimplemented; this presentation change does not add or claim support for it.

## Progressive enhancement

Without JavaScript the original form POST and server responses work. For an explicit AJAX local-login submission, the same handler converts the framework's validated redirect result into a JSON destination. The browser navigates there normally, allowing OIDC to complete at the Web callback. `fetch` does not follow redirects across origins; a `LocalRedirectResult` retains its local URL check before conversion.

Required-field and actual server errors appear beside the form. A failed connection keeps input only in the current form and offers an explicit retry. In-flight submission disables controls and prevents duplicate requests. Passwords are never written to browser storage or URLs; a successful redirect clears the password input.

## Responsive presentation

The page reuses the approved Chinese typography, semantic colors and exact Figma vectors. Content is capped at 1312 CSS pixels and the login card at 512. Below 1100, the compact layout prioritizes a card up to 480 before supporting product information. Below 768, the mobile layout uses normal-sized controls and a shorter introduction. Page, card and error heights grow with content; no whole-page scaling or fixed page height is used.

Figma references: [desktop](https://www.figma.com/design/t3Oui0FU4YphtlOuPpo567?node-id=319-535), [compact](https://www.figma.com/design/t3Oui0FU4YphtlOuPpo567?node-id=341-831), [constraints](https://www.figma.com/design/t3Oui0FU4YphtlOuPpo567?node-id=341-943).

This changes the entry presentation and route authentication requirement, not account ownership, school membership, recovery policy, mail delivery, WeChat authentication or database schema. Verification email delivery and actual external/second-factor providers require their configured environments.

## Account management presentation

The authentication host renders `/Account/Manage` as a Razor page, independently of the WebAssembly workspace. It keeps its own layout file and the original ABP `ManageModel`, profile groups, view components, script bundle and validated return URL. A local layout wraps the theme's Empty layout so framework resources, validation and notifications remain available while the page shares the workspace's white brand shell and form styling. The configured workspace link uses the existing validated account-assistance URL helper. No global theme, credential handler or authorization policy is replaced; the standalone login page remains unchanged.
