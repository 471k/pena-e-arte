# Overnight Prompt — Notification Triggers (2026-06-17)

> **Scope:** Feature 02 (partial) + Feature 06 (main gap) — missing outbound email
> and SMS for six lifecycle events.
>
> Work unsupervised. Commit after every logical unit.
> Do NOT introduce new NuGet packages. Do NOT modify the database schema.

---

## 0. Mandatory Reading (Do This First)

Before writing a single line of code, read these files in order:

```
CLAUDE.md
docs/claude/backend.md
docs/claude/architecture.md
docs/claude/conventions.md
```

Then re-read these four source files to lock the canonical patterns into context:

```
Pena_e_Arte.Infrastructure/Services/NotificationService.cs
Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs
Pena_e_Arte.Infrastructure/Services/MailKit/TemplateRenderer.cs
Pena_e_Arte.Infrastructure/Services/MailKit/Templates/AppointmentConfirmation.html
Pena_e_Arte.Application/Appointments/Commands/SendAppointmentConfirmationCommand.cs
Pena_e_Arte.Infrastructure/Jobs/AppointmentReminderJob.cs
```

Also read the test that demonstrates the exact unit-test pattern to replicate:

```
tests/Pena_e_Arte.UnitTests/Appointments/SendAppointmentConfirmationHandlerTests.cs
```

---

## 1. Problem Statement

The following notification triggers **do not fire any outbound communication**:

| Trigger | Email | SMS |
|---|---|---|
| Appointment created (client books, status = Pending) | ❌ | ❌ |
| Appointment confirmed — SMS path only | ✅ email via `SendAppointmentConfirmationCommand` | ❌ |
| Design approved | ❌ | ❌ |
| Design changes requested | ❌ | ❌ |
| Intake form submitted | ❌ | ❌ |
| Consent form signed | ❌ | ❌ |
| Deposit captured (Stripe OR cash) | ❌ | ❌ |
| Payment refunded | ❌ | ❌ |

---

## 2. Architecture Rules (Do Not Deviate)

These rules must be followed for every piece of code in this task:

1. **No business logic in endpoints.** All logic goes in MediatR handlers.
2. **Every new `Send*Command` handler** must follow the exact pattern in
   `SendAppointmentConfirmationCommand.cs`:
   - Load the entity from `_db` (already scoped to tenant via global query filter).
   - Call `IEmailRenderer` to render the body.
   - `try/catch` the send. Log the `NotificationLog` for BOTH success and failure.
     **Never throw from a notification handler** — a failed email must not abort
     the business transaction.
   - Call `await _realtime.NotifyStudioAsync(studioId, "NotificationReceived", logResponse, ct)`.
3. **SMS:** Always check `client.Phone is not null` before calling
   `SendSmsAsync`. Same try/catch + `NotificationLog` pattern as email.
   SMS body is a short inline string — no template file needed for SMS.
4. **Studio-side notifications** (design review, intake form, consent form)
   send email to `studio.OwnerEmail`. For design notifications, also check
   whether the appointment's `Artist` has a non-empty `Email` and, if so,
   send a second email to the artist.
5. **No new `IgnoreQueryFilters()` calls.** All new handlers are called from
   within normal tenant-scoped handler chains (not Stripe webhooks).
   If a handler is called from `ConfirmPaymentCommand` (the webhook handler
   that already uses `IgnoreQueryFilters()`), load the related entities using
   the same `_db` context that is already in the command — you get tenant
   isolation for free because the StudioId is set explicitly on the entity.
6. **Every new embedded HTML template** must be added as `<EmbeddedResource>`
   in `Pena_e_Arte.Infrastructure/Pena_e_Arte.Infrastructure.csproj`.
7. **Never log PII.** Log levels: use `LogInformation` for successful sends,
   `LogWarning` for failures.
8. **TypeScript / frontend is not touched in this prompt.**

---

## 3. Recipient Matrix

| Trigger | Email Recipient(s) | SMS Recipient |
|---|---|---|
| Appointment created | Client (`client.Email`), Studio (`studio.OwnerEmail`) | Client (`client.Phone`) |
| Appointment confirmed (SMS gap only) | — (already sends email) | Client (`client.Phone`) |
| Design approved | Studio (`studio.OwnerEmail`), Artist (`artist.Email` if non-empty) | none |
| Design changes requested | Studio (`studio.OwnerEmail`), Artist (`artist.Email` if non-empty) | none |
| Intake form submitted | Studio (`studio.OwnerEmail`) | none |
| Consent form signed | Studio (`studio.OwnerEmail`) | none |
| Deposit captured | Client (`client.Email`) | Client (`client.Phone`) |
| Payment refunded | Client (`client.Email`) | Client (`client.Phone`) |

---

## 4. Task List

Work through the tasks in order. Commit after each numbered task.

---

### Task 1 — Extend `IEmailRenderer`

**File:** `Pena_e_Arte.Domain/Interfaces/IEmailRenderer.cs`

Add these method signatures (keep the existing `RenderAppointmentConfirmation`):

```csharp
string RenderAppointmentCreatedClient(
    string clientFirstName,
    DateTime date,
    int durationMinutes,
    string studioName,
    bool showBranding);

string RenderAppointmentCreatedStudio(
    string clientFullName,
    DateTime date,
    int durationMinutes,
    string? notes);

string RenderDesignApproved(
    string artistFirstName,
    string designTitle,
    string? clientNotes,
    bool showBranding);

string RenderDesignChangesRequested(
    string artistFirstName,
    string designTitle,
    string? clientNotes,
    bool showBranding);

string RenderIntakeFormSubmitted(
    string studioName,
    string clientFullName,
    string appointmentDate,
    bool showBranding);

string RenderConsentFormSigned(
    string studioName,
    string clientFullName,
    string appointmentDate,
    bool showBranding);

string RenderDepositCaptured(
    string clientFirstName,
    string amountFormatted,
    string appointmentDate,
    bool showBranding);

string RenderPaymentRefunded(
    string clientFirstName,
    string amountFormatted,
    bool showBranding);
```

**Commit:** `feat(notifications): extend IEmailRenderer with 8 new render methods`

---

### Task 2 — Create HTML Templates

**Location:** `Pena_e_Arte.Infrastructure/Services/MailKit/Templates/`

Create one `.html` file for each new render method. Use the same variable syntax
as `AppointmentConfirmation.html`: `{{variable_name}}` for substitution,
`{{#if variable}}…{{/if}}` for optional blocks.

Check `TemplateRenderer.cs` to confirm the exact regex — variables are word
characters `\w+` only (no dots, no dashes).

#### 2a. `AppointmentCreatedClient.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Booking Received</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Booking request received</h2>
  <p style="color:#555;margin-top:0">Hi {{client_first_name}},</p>
  <p>Your booking request at <strong>{{studio_name}}</strong> has been received
     and is pending confirmation from the studio.</p>
  <table style="width:100%;border-collapse:collapse;margin:16px 0">
    <tr><td style="padding:8px 0;border-bottom:1px solid #e5e5e5;color:#555;width:40%">Date</td>
        <td style="padding:8px 0;border-bottom:1px solid #e5e5e5;font-weight:600">{{appointment_date}}</td></tr>
    <tr><td style="padding:8px 0;color:#555">Duration</td>
        <td style="padding:8px 0;font-weight:600">{{duration_minutes}} min</td></tr>
  </table>
  <p style="color:#555;font-size:13px">You will receive another email once the studio confirms your appointment.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Powered by Pena e Artë</p>
  {{/if}}
</body>
</html>
```

Variables: `client_first_name`, `studio_name`, `appointment_date`,
`duration_minutes`, `show_branding`.

#### 2b. `AppointmentCreatedStudio.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>New Booking Request</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">New booking request</h2>
  <p style="color:#555;margin-top:0"><strong>{{client_full_name}}</strong> has submitted a booking request.</p>
  <table style="width:100%;border-collapse:collapse;margin:16px 0">
    <tr><td style="padding:8px 0;border-bottom:1px solid #e5e5e5;color:#555;width:40%">Date</td>
        <td style="padding:8px 0;border-bottom:1px solid #e5e5e5;font-weight:600">{{appointment_date}}</td></tr>
    <tr><td style="padding:8px 0;color:#555">Duration</td>
        <td style="padding:8px 0;font-weight:600">{{duration_minutes}} min</td></tr>
  </table>
  {{#if show_notes}}
  <p><strong>Notes:</strong> {{notes}}</p>
  {{/if}}
  <p style="color:#555;font-size:13px">Log in to the studio dashboard to confirm or decline this request.</p>
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Pena e Artë Studio Platform</p>
</body>
</html>
```

Variables: `client_full_name`, `appointment_date`, `duration_minutes`,
`notes`, `show_notes`.

#### 2c. `DesignApproved.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Design Approved</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Design approved ✓</h2>
  <p style="color:#555;margin-top:0">Hi {{artist_first_name}},</p>
  <p>Your client has <strong>approved</strong> the design
     <em>{{design_title}}</em>.</p>
  {{#if client_notes}}
  <blockquote style="border-left:3px solid #e5e5e5;margin:16px 0;padding:8px 16px;color:#555">
    {{client_notes}}
  </blockquote>
  {{/if}}
  <p style="color:#555;font-size:13px">The appointment can now proceed. Log in to view the full design history.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Powered by Pena e Artë</p>
  {{/if}}
</body>
</html>
```

Variables: `artist_first_name`, `design_title`, `client_notes`, `show_branding`.

#### 2d. `DesignChangesRequested.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Design Changes Requested</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Changes requested</h2>
  <p style="color:#555;margin-top:0">Hi {{artist_first_name}},</p>
  <p>Your client has requested <strong>changes</strong> to the design
     <em>{{design_title}}</em>.</p>
  {{#if client_notes}}
  <blockquote style="border-left:3px solid #f59e0b;margin:16px 0;padding:8px 16px;color:#555">
    {{client_notes}}
  </blockquote>
  {{/if}}
  <p style="color:#555;font-size:13px">Log in to review the feedback and upload a revised design.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Powered by Pena e Artë</p>
  {{/if}}
</body>
</html>
```

Variables: `artist_first_name`, `design_title`, `client_notes`, `show_branding`.

#### 2e. `IntakeFormSubmitted.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Intake Form Submitted</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Intake form submitted</h2>
  <p style="color:#555;margin-top:0"><strong>{{client_full_name}}</strong> has submitted their
     intake form for their appointment on <strong>{{appointment_date}}</strong>.</p>
  <p style="color:#555;font-size:13px">Log in to {{studio_name}} dashboard to review the form.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Pena e Artë Studio Platform</p>
  {{/if}}
</body>
</html>
```

Variables: `client_full_name`, `appointment_date`, `studio_name`, `show_branding`.

#### 2f. `ConsentFormSigned.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Consent Form Signed</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Consent form signed</h2>
  <p style="color:#555;margin-top:0"><strong>{{client_full_name}}</strong> has signed the
     consent form for their appointment on <strong>{{appointment_date}}</strong>.</p>
  <p style="color:#555;font-size:13px">Log in to {{studio_name}} dashboard to review the signed form.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Pena e Artë Studio Platform</p>
  {{/if}}
</body>
</html>
```

Variables: `client_full_name`, `appointment_date`, `studio_name`, `show_branding`.

#### 2g. `DepositCaptured.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Deposit Received</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Deposit received</h2>
  <p style="color:#555;margin-top:0">Hi {{client_first_name}},</p>
  <p>Your deposit of <strong>{{amount_formatted}}</strong> has been received and your
     appointment on <strong>{{appointment_date}}</strong> is now secured.</p>
  <p style="color:#555;font-size:13px">See you soon!</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Powered by Pena e Artë</p>
  {{/if}}
</body>
</html>
```

Variables: `client_first_name`, `amount_formatted`, `appointment_date`,
`show_branding`.

#### 2h. `PaymentRefunded.html`

```html
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"><title>Refund Processed</title></head>
<body style="font-family:sans-serif;color:#1a1a1a;max-width:560px;margin:0 auto;padding:24px">
  <h2 style="margin-bottom:4px">Refund processed</h2>
  <p style="color:#555;margin-top:0">Hi {{client_first_name}},</p>
  <p>A refund of <strong>{{amount_formatted}}</strong> has been processed to your original
     payment method. Please allow 3–5 business days for the funds to appear.</p>
  <p style="color:#555;font-size:13px">If you have any questions, please contact the studio directly.</p>
  {{#if show_branding}}
  <hr style="border:none;border-top:1px solid #e5e5e5;margin:24px 0">
  <p style="font-size:11px;color:#aaa;text-align:center">Powered by Pena e Artë</p>
  {{/if}}
</body>
</html>
```

Variables: `client_first_name`, `amount_formatted`, `show_branding`.

**After creating all eight `.html` files**, update
`Pena_e_Arte.Infrastructure/Pena_e_Arte.Infrastructure.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Services\MailKit\Templates\AppointmentConfirmation.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\AppointmentCreatedClient.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\AppointmentCreatedStudio.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\DesignApproved.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\DesignChangesRequested.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\IntakeFormSubmitted.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\ConsentFormSigned.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\DepositCaptured.html" />
  <EmbeddedResource Include="Services\MailKit\Templates\PaymentRefunded.html" />
</ItemGroup>
```

> **Note:** Check first whether the existing .csproj uses a wildcard glob
> (`**/*.html`) for embedded resources — if so, no individual entries are
> needed and you should leave the .csproj unchanged for the template files.

Run `dotnet build` — it must succeed before continuing.

**Commit:** `feat(notifications): add 8 email templates as embedded resources`

---

### Task 3 — Implement New `EmailRenderer` Methods

**File:** `Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs`

Follow the exact pattern of the existing `_confirmationTemplate` field and
`RenderAppointmentConfirmation` method:

1. Declare a private `readonly string` field for each new template, initialised
   with `LoadEmbeddedTemplate("TemplateName.html")` in the constructor.
2. Implement each interface method using `TemplateRenderer.Render(template, vars)`
   where `vars` is a `Dictionary<string, string>`.

Key implementation notes:
- For `bool showBranding`: convert to `"true"` or `string.Empty`
  (an empty string evaluates falsy in the `{{#if}}` block — see how
  `AppointmentConfirmation.html` handles `show_notes`).
- For `string? clientNotes` / `string? notes`: same pattern — pass the value
  or `string.Empty`; set the corresponding `show_*` key accordingly.
- Format `DateTime date` as `date.ToString("dddd, dd MMMM yyyy HH:mm",
  CultureInfo.InvariantCulture)` for consistency with the existing confirmation template.
- Format `decimal amount` as `amount.ToString("C", new CultureInfo("pt-PT"))`
  (Euro locale used throughout the app).

Run `dotnet build` — it must succeed before continuing.

**Commit:** `feat(notifications): implement EmailRenderer for all new templates`

---

### Task 4 — Add SMS to `SendAppointmentConfirmationCommand`

**File:** `Pena_e_Arte.Application/Appointments/Commands/SendAppointmentConfirmationCommand.cs`

After the existing email send block (inside the handler's `Handle` method),
add an SMS block:

```csharp
// SMS — only if client has a phone number
if (appointment.Client?.Phone is not null)
{
    string smsBody =
        $"Hi {appointment.Client.FirstName}, your tattoo appointment at " +
        $"{studio.Name} on {appointment.Date:dd MMM yyyy 'at' HH:mm} is confirmed. " +
        $"See you soon!";

    bool smsSent = true;
    try
    {
        await _notifications.SendSmsAsync(appointment.Client.Phone, smsBody, ct);
    }
    catch (Exception ex)
    {
        smsSent = false;
        _logger.LogWarning(ex,
            "SMS confirmation failed for appointment {AppointmentId} tenant {TenantId}",
            command.AppointmentId, studio.Id);
    }

    NotificationLog smsLog = new()
    {
        StudioId      = studio.Id,
        RecipientId   = appointment.ClientId,
        RecipientType = NotificationRecipientType.Client,
        Channel       = NotificationChannel.Sms,
        Subject       = "Appointment Confirmation",
        Body          = smsBody,
        SentAt        = DateTime.UtcNow,
        IsSuccess     = smsSent,
    };
    _db.NotificationLogs.Add(smsLog);
    await _db.SaveChangesAsync(ct);
}
```

> The `appointment` and `studio` variables already exist in the handler at
> this point. If `appointment.Client` is not eagerly loaded, add
> `.Include(a => a.Client)` to the existing query at the top of `Handle`.

Run `dotnet build` — it must succeed before continuing.

**Commit:** `feat(notifications): add SMS to appointment confirmation handler`

---

### Task 5 — `SendAppointmentCreatedNotificationCommand`

This is a new MediatR command + handler that fires when a client books an
appointment (status = Pending). It sends:
- Email to client (booking request received)
- Email to studio owner (new booking request)
- SMS to client (if phone exists)

#### 5a. Command record

**File:** `Pena_e_Arte.Application/Appointments/Commands/SendAppointmentCreatedNotificationCommand.cs`

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAppointmentCreatedNotificationCommand(Guid AppointmentId) : IRequest;
```

#### 5b. Handler

**File:** `Pena_e_Arte.Application/Appointments/Commands/SendAppointmentCreatedNotificationHandler.cs`

Constructor dependencies (same as `SendAppointmentConfirmationHandler`):
`AppDbContext _db`, `IEmailRenderer _emailRenderer`,
`INotificationService _notifications`, `IRealtimeNotifier _realtime`,
`ILogger<SendAppointmentCreatedNotificationHandler> _logger`.

`Handle` implementation:

1. Load appointment with `Include(a => a.Client)` and `Include(a => a.Artist)`.
   If not found, log a warning and return.
2. Load studio. If not found, log a warning and return.
3. Format `string appointmentDate = appointment.Date.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)`.
4. **Client email:**
   ```
   subject  = $"Booking request received — {studio.Name}"
   body     = _emailRenderer.RenderAppointmentCreatedClient(
                   appointment.Client.FirstName,
                   appointment.Date,
                   appointment.DurationMinutes,
                   studio.Name,
                   studio.ShowBranding)
   ```
   try/catch → `NotificationLog { RecipientId = appointment.ClientId, RecipientType = Client, Channel = Email }` → SaveChanges.
5. **Studio email:**
   ```
   subject  = $"New booking request from {appointment.Client.FirstName} {appointment.Client.LastName}"
   body     = _emailRenderer.RenderAppointmentCreatedStudio(
                   $"{appointment.Client.FirstName} {appointment.Client.LastName}",
                   appointment.Date,
                   appointment.DurationMinutes,
                   appointment.Notes)
   ```
   Send to `studio.OwnerEmail`.
   try/catch → `NotificationLog { RecipientId = studio.Id, RecipientType = Studio, Channel = Email }` → SaveChanges.
   > If `NotificationRecipientType` does not have a `Studio` value, check the enum
   > in `Pena_e_Arte.Domain/Enums/`. If missing, add `Studio` to the enum.
6. **Client SMS** (if `appointment.Client.Phone is not null`):
   ```
   smsBody = $"Hi {appointment.Client.FirstName}, your booking request at {studio.Name} " +
             $"on {appointmentDate} has been received and is pending confirmation."
   ```
   try/catch → `NotificationLog { Channel = Sms }` → SaveChanges.
7. `await _realtime.NotifyStudioAsync(studio.Id, "NotificationReceived", logResponse, ct)`
   where `logResponse` is a `NotificationLogResponse` built from the client email log.
   Use the same mapping as `SendAppointmentConfirmationHandler` — check that file for
   how `NotificationLogResponse` is constructed.

**Commit:** `feat(notifications): add SendAppointmentCreatedNotification command + handler`

---

### Task 6 — `SendDesignReviewNotificationCommand`

Fires when a client approves or requests changes on a design revision.

#### 6a. Command record

**File:** `Pena_e_Arte.Application/Designs/Commands/SendDesignReviewNotificationCommand.cs`

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Designs.Commands;

public record SendDesignReviewNotificationCommand(Guid DesignRevisionId, bool Approved) : IRequest;
```

#### 6b. Handler

**File:** `Pena_e_Arte.Application/Designs/Commands/SendDesignReviewNotificationHandler.cs`

Constructor dependencies: `AppDbContext _db`, `IEmailRenderer _emailRenderer`,
`INotificationService _notifications`, `IRealtimeNotifier _realtime`,
`ILogger<SendDesignReviewNotificationHandler> _logger`.

`Handle` implementation:

1. Load `DesignRevision` and navigate to its parent `Design` (Include as needed).
   If not found, log warning and return.
2. Load `Studio`. If not found, log warning and return.
3. Determine artist:
   - If `design.ArtistId` (or equivalent FK — check the `Design` and `DesignRevision`
     entities for the correct FK name) is non-null, load the `Artist` entity.
   - If found and `artist.Email` is non-empty, also send to artist.
4. Choose template:
   ```
   bool approved = command.Approved;
   string subject = approved
       ? $"Design approved — {designRevision.Title}"
       : $"Changes requested — {designRevision.Title}";
   string body = approved
       ? _emailRenderer.RenderDesignApproved(
             artist?.FirstName ?? "there",
             designRevision.Title,
             designRevision.ClientNotes,
             studio.ShowBranding)
       : _emailRenderer.RenderDesignChangesRequested(
             artist?.FirstName ?? "there",
             designRevision.Title,
             designRevision.ClientNotes,
             studio.ShowBranding);
   ```
   > Check the actual `DesignRevision` entity for the correct property names
   > (`Title`, `ClientNotes`, etc.). Adjust if they differ.
5. **Email to studio owner:**
   try/catch → `NotificationLog { RecipientId = studio.Id, RecipientType = Studio, Channel = Email }` → SaveChanges.
6. **Email to artist** (if artist email is non-empty and different from `studio.OwnerEmail`):
   Same subject and body. try/catch → `NotificationLog { RecipientId = artist.Id, RecipientType = Artist }`.
   > If `NotificationRecipientType` does not have an `Artist` value, add it to the enum.
7. SignalR: `NotifyStudioAsync(studio.Id, "NotificationReceived", logResponse, ct)`.

**Commit:** `feat(notifications): add SendDesignReviewNotification command + handler`

---

### Task 7 — `SendIntakeFormSubmittedNotificationCommand`

#### 7a. Command record

**File:** `Pena_e_Arte.Application/Forms/Commands/SendIntakeFormSubmittedNotificationCommand.cs`

> If the namespace/folder for forms commands is different (e.g., `Consultations`
> or `IntakeForms`), use the existing convention — check the file system first.

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Forms.Commands;

public record SendIntakeFormSubmittedNotificationCommand(Guid IntakeFormId) : IRequest;
```

#### 7b. Handler

**File:** `Pena_e_Arte.Application/Forms/Commands/SendIntakeFormSubmittedNotificationHandler.cs`

`Handle` implementation:

1. Load `IntakeForm` with client and appointment navigations (Include as needed).
   Check the actual entity and FK names. If not found, log warning and return.
2. Load `Studio`.
3. Format appointment date as a string (if intake form links to an appointment).
   If the intake form is not linked to an appointment, use `"(no appointment date)"`.
4. **Email to studio owner:**
   ```
   subject = $"Intake form submitted — {client.FirstName} {client.LastName}"
   body    = _emailRenderer.RenderIntakeFormSubmitted(
                 studio.Name,
                 $"{client.FirstName} {client.LastName}",
                 appointmentDate,
                 studio.ShowBranding)
   ```
   try/catch → `NotificationLog` → SaveChanges.
5. SignalR: `NotifyStudioAsync`.

**Commit:** `feat(notifications): add SendIntakeFormSubmittedNotification command + handler`

---

### Task 8 — `SendConsentFormSignedNotificationCommand`

#### 8a. Command record

**File:** `Pena_e_Arte.Application/Forms/Commands/SendConsentFormSignedNotificationCommand.cs`

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Forms.Commands;

public record SendConsentFormSignedNotificationCommand(Guid ConsentFormId) : IRequest;
```

#### 8b. Handler

**File:** `Pena_e_Arte.Application/Forms/Commands/SendConsentFormSignedNotificationHandler.cs`

Same structure as Task 7 — load `ConsentForm`, load studio, send email to
`studio.OwnerEmail` using `RenderConsentFormSigned`, write `NotificationLog`,
push SignalR.

**Commit:** `feat(notifications): add SendConsentFormSignedNotification command + handler`

---

### Task 9 — `SendDepositCapturedNotificationCommand`

Reused by **both** `CaptureDepositCommand` (Stripe) and
`ConfirmCashDepositCommand` (cash).

#### 9a. Command record

**File:** `Pena_e_Arte.Application/Payments/Commands/SendDepositCapturedNotificationCommand.cs`

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Payments.Commands;

public record SendDepositCapturedNotificationCommand(Guid PaymentId) : IRequest;
```

#### 9b. Handler

**File:** `Pena_e_Arte.Application/Payments/Commands/SendDepositCapturedNotificationHandler.cs`

`Handle` implementation:

1. Load `Payment` (check the actual entity name — it may be `StudioPayment`,
   `Deposit`, `PaymentRecord`, etc. — grep the Domain project for the entity).
   Include the linked `Appointment` and `Client`. If not found, log warning and return.
2. Load `Studio`.
3. Format amount: `payment.Amount.ToString("C", new CultureInfo("pt-PT"))`.
4. Format appointment date.
5. **Email to client:**
   ```
   subject = "Deposit received — your appointment is secured"
   body    = _emailRenderer.RenderDepositCaptured(
                 client.FirstName,
                 amountFormatted,
                 appointmentDate,
                 studio.ShowBranding)
   ```
   try/catch → `NotificationLog { RecipientId = client.Id, RecipientType = Client, Channel = Email }` → SaveChanges.
6. **SMS to client** (if `client.Phone is not null`):
   ```
   smsBody = $"Hi {client.FirstName}, your deposit of {amountFormatted} " +
             $"for your appointment on {appointmentDate} has been received. See you soon!"
   ```
   try/catch → `NotificationLog { Channel = Sms }` → SaveChanges.
7. SignalR: `NotifyStudioAsync`.

**Commit:** `feat(notifications): add SendDepositCapturedNotification command + handler`

---

### Task 10 — `SendPaymentRefundedNotificationCommand`

#### 10a. Command record

**File:** `Pena_e_Arte.Application/Payments/Commands/SendPaymentRefundedNotificationCommand.cs`

```csharp
using MediatR;

namespace Pena_e_Arte.Application.Payments.Commands;

public record SendPaymentRefundedNotificationCommand(Guid PaymentId) : IRequest;
```

#### 10b. Handler

Same structure as Task 9. Load payment → client → studio. Send email with
`RenderPaymentRefunded(client.FirstName, amountFormatted, studio.ShowBranding)`.
SMS with a brief refund message if `client.Phone is not null`.
Write `NotificationLog` entries. Push SignalR.

**Commit:** `feat(notifications): add SendPaymentRefundedNotification command + handler`

---

### Task 11 — Wire New Commands into Existing Handlers

For each handler listed, inject `ISender _sender` if it is not already a
dependency, then call the new command **after** the main business logic
completes (same pattern as `ConfirmAppointmentCommand` calling
`SendAppointmentConfirmationCommand`).

#### 11a. `CreateAppointmentCommand` handler

**After** saving the appointment and scheduling reminders, add:

```csharp
await sender.Send(new SendAppointmentCreatedNotificationCommand(appointment.Id), ct);
```

#### 11b. `ReviewDesignCommand` handler

**After** setting the approval status and saving, add:

```csharp
await sender.Send(
    new SendDesignReviewNotificationCommand(revisionId, command.Approved), ct);
```

> Check the handler for the local variable name holding the revision ID.

#### 11c. `SubmitIntakeFormCommand` handler (or `SubmitIntakeFormHandler`)

Check whether `ISender` is already injected. If not, add it to the constructor.
**After** saving the form:

```csharp
await sender.Send(new SendIntakeFormSubmittedNotificationCommand(intakeForm.Id), ct);
```

#### 11d. `SignConsentFormCommand` handler

Same as 11c — inject `ISender` if missing, then:

```csharp
await sender.Send(new SendConsentFormSignedNotificationCommand(consentForm.Id), ct);
```

#### 11e. `CaptureDepositCommand` handler

**After** the Stripe capture and status update:

```csharp
await sender.Send(new SendDepositCapturedNotificationCommand(payment.Id), ct);
```

#### 11f. `ConfirmCashDepositCommand` handler

Same command, same call site pattern:

```csharp
await sender.Send(new SendDepositCapturedNotificationCommand(payment.Id), ct);
```

#### 11g. `RefundPaymentCommand` handler

**After** calling Stripe refund and updating status:

```csharp
await sender.Send(new SendPaymentRefundedNotificationCommand(payment.Id), ct);
```

Run `dotnet build` — must be green before continuing.

**Commit:** `feat(notifications): wire notification commands into 7 existing handlers`

---

### Task 12 — Unit Tests

Create one test class per new handler. Follow the exact structure of
`SendAppointmentConfirmationHandlerTests.cs`:

- `FakeDbContext.Create()` for the in-memory DB
- `NSubstitute.For<IEmailRenderer>()`, `NSubstitute.For<INotificationService>()`,
  `NSubstitute.For<IRealtimeNotifier>()`, `NullLogger<THandler>.Instance`
- A `SeedData` helper that inserts all required entities and returns their IDs
- A `CreateSut()` factory method

#### Required test cases for **each** new handler:

| Test | Assertion |
|---|---|
| `Handle_ValidInput_SendsEmail` | `_notifications.Received(1).SendEmailAsync(...)` |
| `Handle_EntityNotFound_DoesNotSendEmail` | `_notifications.DidNotReceive().SendEmailAsync(...)` |
| `Handle_EmailFails_DoesNotThrow` | `act.Should().NotThrowAsync()` |
| `Handle_EmailFails_WritesFailedNotificationLog` | log in DB with `IsSuccess = false` |
| `Handle_ValidInput_WritesSuccessNotificationLog` | log in DB with `IsSuccess = true` |
| `Handle_ValidInput_PushesNotificationReceivedEvent` | `_realtime.Received(1).NotifyStudioAsync(...)` |

#### Additional test cases for handlers that send SMS:

| Test | Assertion |
|---|---|
| `Handle_ClientHasPhone_SendsSms` | `_notifications.Received(1).SendSmsAsync(...)` |
| `Handle_ClientHasNoPhone_DoesNotSendSms` | `_notifications.DidNotReceive().SendSmsAsync(...)` |
| `Handle_SmsFails_DoesNotThrow` | `act.Should().NotThrowAsync()` |
| `Handle_SmsFails_WritesFailedSmsLog` | log in DB with `Channel = Sms, IsSuccess = false` |

#### Test for `SendAppointmentConfirmationHandler` (existing class, new SMS tests)

Add to `SendAppointmentConfirmationHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ClientHasPhone_SendsSms()
{
    // Seed a client with a phone number
    (Guid appointmentId, _) = await SeedDataWithPhone(showBranding: true);

    await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

    await _notifications.Received(1)
        .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Handle_ClientHasNoPhone_DoesNotSendSms()
{
    (Guid appointmentId, _) = await SeedData(showBranding: true); // existing helper — no phone

    await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

    await _notifications.DidNotReceive()
        .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

Add a `SeedDataWithPhone` helper that is a copy of `SeedData` but sets
`client.Phone = "+351912345678"`.

#### Test file locations:

```
tests/Pena_e_Arte.UnitTests/Appointments/SendAppointmentCreatedNotificationHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Designs/SendDesignReviewNotificationHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Forms/SendIntakeFormSubmittedNotificationHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Forms/SendConsentFormSignedNotificationHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Payments/SendDepositCapturedNotificationHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Payments/SendPaymentRefundedNotificationHandlerTests.cs
```

Create any missing folders.

Run `dotnet test` — all tests must pass before committing.

**Commit:** `test(notifications): unit tests for all new notification handlers`

---

### Task 13 — Integration Tests

**File:** `tests/Pena_e_Arte.IntegrationTests/Notifications/NotificationDispatchTests.cs`

Write integration tests that drive each **wiring** point end-to-end through the
MediatR pipeline.

For each trigger, seed the DB, send the parent command (e.g.,
`CreateAppointmentCommand`), then assert that a `NotificationLog` row exists in
the DB with the expected `Channel`, `IsSuccess`, and `RecipientType`.

Use the existing integration test setup pattern — check
`tests/Pena_e_Arte.IntegrationTests/` for the base class or test fixture.
If there is a `WebApplicationFactory` or `IntegrationTestBase`, use it.
Do NOT call `_notifications.SendEmailAsync` directly — let the real MediatR
pipeline invoke the handler.

Minimum test coverage:

| Test | Trigger command | Assert |
|---|---|---|
| `CreateAppointment_SendsClientAndStudioEmail` | `CreateAppointmentCommand` | 2 Email logs |
| `ConfirmAppointment_SendsSmsWhenClientHasPhone` | `ConfirmAppointmentCommand` | Sms log |
| `ReviewDesign_Approved_SendsEmailToStudio` | `ReviewDesignCommand(Approved=true)` | Email log, recipient = Studio |
| `ReviewDesign_ChangesRequested_SendsEmailToStudio` | `ReviewDesignCommand(Approved=false)` | Email log |
| `SubmitIntakeForm_SendsEmailToStudio` | `SubmitIntakeFormCommand` | Email log |
| `SignConsentForm_SendsEmailToStudio` | `SignConsentFormCommand` | Email log |
| `CaptureDeposit_SendsEmailAndSmsToClient` | `CaptureDepositCommand` | Email + Sms logs |
| `RefundPayment_SendsEmailToClient` | `RefundPaymentCommand` | Email log |

> In the integration test environment, `INotificationService` will be the real
> `NotificationService` or a test double — check what the integration test base
> already registers and follow that pattern. Do NOT instantiate a live SMTP
> or Twilio connection in CI. If the base class already substitutes
> `INotificationService`, the real sends will be no-ops and only the
> `NotificationLog` rows are verifiable.

Run `dotnet test` — all tests must pass.

**Commit:** `test(notifications): integration tests for notification dispatch chain`

---

### Task 14 — Final Verification

1. `dotnet build` — zero errors, zero warnings introduced by this work.
2. `dotnet test` — all tests pass (do not skip any).
3. Grep for any new `IgnoreQueryFilters()` calls introduced in this session —
   there must be **none**. If any were added, remove them.
4. Grep for `Console.WriteLine` or `console.log` — none in production paths.
5. Grep for any hardcoded email addresses or phone numbers in source code
   (other than test helpers) — none allowed.
6. Verify the `NotificationRecipientType` enum covers all values used:
   `Client`, `Studio`, `Artist`. If `Studio` or `Artist` were added, ensure
   the enum is in `Pena_e_Arte.Domain/Enums/` and that a corresponding EF Core
   migration is **not** needed (enums stored as `int` — no migration required
   unless the column type is `varchar`; check the `NotificationLog` configuration
   in `AppDbContext` or the Infrastructure `ModelBuilder`).
7. Run `pnpm lint` in `frontend/` — must pass (frontend was not touched, but
   confirm there are no pre-existing lint failures that could be blamed on
   this session).
8. Do a final `git log --oneline -20` and confirm all commits from this session
   are present with the expected messages.

**Commit:** (none — this task is verification only)

---

## 5. What Not To Do

- Do not create a `NotificationEventType` enum or add any new column to
  `NotificationLog` — the existing schema is sufficient.
- Do not create a Hangfire job for any of these triggers — they are all
  synchronous sends dispatched inline from the handler.
- Do not modify any existing HTML template.
- Do not modify any existing FluentValidation validator — the new `Send*Command`
  records do not need validators (they are internal dispatch commands, not
  user-facing requests).
- Do not add any new NuGet package.
- Do not add `AllowAnonymous` to any endpoint.
- Do not add `IgnoreQueryFilters()` anywhere.
- Do not touch the frontend.

---

## 6. If You Get Stuck

- **Entity property names:** Always grep/read the actual entity file before
  assuming a property name. The summary above may not reflect the exact naming.
- **Namespace for forms handlers:** The intake form and consent form handlers
  may live under `Consultations`, `Forms`, `ConsentForms`, or similar. Check
  the Application project directory structure with `ls` before creating files.
- **`NotificationRecipientType` enum:** Read the file at
  `Pena_e_Arte.Domain/Enums/NotificationRecipientType.cs` before adding values.
- **Payment entity name:** Read the Domain entities folder to find the correct
  name (`Payment`, `StudioPayment`, `DepositRecord`, etc.).
- **`ShowBranding` property:** Verify the exact property name on `Studio`
  (it may be `ShowBranding`, `DisplayBranding`, or similar — read the entity).
- **`ISender` already injected:** Many handlers already receive `ISender` via
  constructor. Do not add a duplicate parameter — check the existing constructor
  first.
