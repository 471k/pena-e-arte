import { PublicContentLayout } from "./PublicContentLayout";

// Whether /contact should be a monitored inbox or a contact form is open question
// §3.5 (founder decision). Shipped as a monitored-inbox placeholder; if a form is
// chosen later, add name/email/message only and list it in the Privacy Policy's
// sub-processor/retention inventory.
export function ContactPage() {
  return (
    <PublicContentLayout
      title="Contact — TattooOS"
      description="Get in touch with the TattooOS team."
      canonicalPath="/contact"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Contact</h1>
      <p className="mt-3 text-sm text-muted-foreground">
        For support, privacy requests (including data access or erasure), or general
        enquiries, email us at{" "}
        <a
          href="mailto:support@tattooos.co"
          className="underline underline-offset-2 hover:text-foreground"
        >
          support@tattooos.co
        </a>
        .
      </p>
    </PublicContentLayout>
  );
}
