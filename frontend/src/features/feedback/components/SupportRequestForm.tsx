import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { cn } from "@/shared/utils/cn";
import { useSubmitFeedbackMutation } from "../feedbackApi";

const schema = z.object({
  title: z.string().min(1, "Subject is required").max(150, "Max 150 characters"),
  body:  z.string().min(10, "Please describe your issue in at least 10 characters").max(2000, "Max 2000 characters"),
});
type FormValues = z.infer<typeof schema>;

export function SupportRequestForm() {
  const [submitFeedback, { isLoading }] = useSubmitFeedbackMutation();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", body: "" },
  });

  async function onSubmit(values: FormValues) {
    try {
      await submitFeedback({ type: "SupportRequest", title: values.title, body: values.body }).unwrap();
      toast.success("Support request sent — we'll reply here.");
      reset();
    } catch {
      toast.error("Failed to send. Please try again.");
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1">
      <p className="text-sm text-muted-foreground">
        Can't find what you need in Guides or FAQ? Send us a message and we'll reply here.
      </p>

      <div className="space-y-1.5">
        <Label htmlFor="support-title">Subject</Label>
        <Input
          id="support-title"
          placeholder="Brief summary"
          disabled={isLoading}
          {...register("title")}
          className={cn(errors.title && "border-destructive")}
        />
        {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="support-body">Message</Label>
        <Textarea
          id="support-body"
          rows={5}
          placeholder="Describe what you need help with…"
          disabled={isLoading}
          {...register("body")}
          className={cn("resize-none", errors.body && "border-destructive")}
        />
        {errors.body && <p className="text-xs text-destructive">{errors.body.message}</p>}
      </div>

      <Button type="submit" size="sm" disabled={isLoading} className="w-full">
        {isLoading && <Loader2 className="h-4 w-4 animate-spin mr-1.5" />}
        Send message
      </Button>
    </form>
  );
}
