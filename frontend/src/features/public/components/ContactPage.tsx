import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { PublicContentLayout } from "./PublicContentLayout";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { cn } from "@/shared/utils/cn";
import { useSubmitContactMutation } from "../contactApi";

const schema = z.object({
  name: z.string().min(1, "Please enter your name").max(100),
  email: z.string().min(1, "Please enter your email").email("Enter a valid email").max(200),
  message: z.string().min(1, "Please enter a message").max(2000, "Message is too long (max 2000)"),
});

type FormValues = z.infer<typeof schema>;

// Public contact form. Submissions relay to support by email (send-only, not persisted).
export function ContactPage() {
  const [submitContact, { isLoading }] = useSubmitContactMutation();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", email: "", message: "" },
  });

  async function onSubmit(values: FormValues) {
    const result = await submitContact(values);
    if ("data" in result) {
      toast.success("Message sent — we'll get back to you by email.");
      reset();
    } else {
      toast.error("Couldn't send your message. Please try again, or email us directly.");
    }
  }

  return (
    <PublicContentLayout
      title="Contact — TattooOS"
      description="Get in touch with the TattooOS team."
      canonicalPath="/contact"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Contact</h1>
      <p className="mt-3 text-sm text-muted-foreground">
        Questions, support, or privacy requests (including data access or erasure)? Send us a
        message and we&apos;ll reply by email. You can also email us directly at{" "}
        <a
          href="mailto:support@tattooos.co"
          className="underline underline-offset-2 hover:text-foreground"
        >
          support@tattooos.co
        </a>
        .
      </p>

      <form onSubmit={handleSubmit(onSubmit)} className="mt-6 max-w-lg space-y-4" noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="name">Name</Label>
          <Input
            id="name"
            autoComplete="name"
            disabled={isLoading}
            {...register("name")}
            className={cn(errors.name && "border-destructive")}
          />
          {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="email">Email</Label>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            disabled={isLoading}
            {...register("email")}
            className={cn(errors.email && "border-destructive")}
          />
          {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="message">Message</Label>
          <Textarea
            id="message"
            rows={5}
            disabled={isLoading}
            {...register("message")}
            className={cn("resize-y", errors.message && "border-destructive")}
          />
          {errors.message && <p className="text-xs text-destructive">{errors.message.message}</p>}
        </div>

        <Button type="submit" disabled={isLoading}>
          {isLoading ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Sending…
            </>
          ) : (
            "Send message"
          )}
        </Button>
      </form>
    </PublicContentLayout>
  );
}
