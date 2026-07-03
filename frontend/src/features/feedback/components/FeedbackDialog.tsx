import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2, CheckCircle } from "lucide-react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/shared/components/ui/dialog";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { cn } from "@/shared/utils/cn";
import { FEEDBACK_TYPE } from "../feedback.types";
import { useSubmitFeedbackMutation } from "../feedbackApi";

const schema = z.object({
  type:  z.enum(["BugReport", "FeatureRequest", "General"]),
  title: z.string().min(1, "Title is required").max(150, "Max 150 characters"),
  body:  z.string().min(10, "Please describe in at least 10 characters").max(2000, "Max 2000 characters"),
});
type FormValues = z.infer<typeof schema>;

interface FeedbackDialogProps {
  open:         boolean;
  onOpenChange: (open: boolean) => void;
}

export function FeedbackDialog({ open, onOpenChange }: FeedbackDialogProps) {
  const [submitted, setSubmitted] = useState(false);
  const [submitFeedback, { isLoading }] = useSubmitFeedbackMutation();

  const {
    register,
    control,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { type: "BugReport", title: "", body: "" },
  });

  const bodyLength = watch("body").length;

  async function onSubmit(values: FormValues) {
    try {
      await submitFeedback(values).unwrap();
      setSubmitted(true);
      reset();
    } catch {
      toast.error("Failed to submit. Please try again.");
    }
  }

  function handleClose(nextOpen: boolean) {
    if (!nextOpen) {
      setSubmitted(false);
      reset();
    }
    onOpenChange(nextOpen);
  }

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Send Feedback</DialogTitle>
          <DialogDescription>
            Report a bug, request a feature, or share your thoughts.
            Our team reviews every submission.
          </DialogDescription>
        </DialogHeader>

        {submitted ? (
          <div className="flex flex-col items-center gap-3 py-6 text-center">
            <CheckCircle className="h-10 w-10 text-green-500" />
            <p className="text-sm font-medium">Thank you for your feedback!</p>
            <p className="text-xs text-muted-foreground">
              We&apos;ve received your message and will review it soon.
            </p>
            <Button size="sm" onClick={() => handleClose(false)} className="mt-2">
              Close
            </Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1">
            <div className="space-y-1.5">
              <Label htmlFor="feedback-type">Type</Label>
              <Controller
                control={control}
                name="type"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="feedback-type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={FEEDBACK_TYPE.BugReport}>🐛 Bug Report</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.FeatureRequest}>✨ Feature Request</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.General}>💬 General Feedback</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="feedback-title">Title</Label>
              <Input
                id="feedback-title"
                placeholder="Brief summary"
                disabled={isLoading}
                {...register("title")}
                className={cn(errors.title && "border-destructive")}
              />
              {errors.title && (
                <p className="text-xs text-destructive">{errors.title.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="feedback-body">Description</Label>
                <span className={cn(
                  "text-xs",
                  bodyLength > 1800 ? "text-amber-500" : "text-muted-foreground"
                )}>
                  {bodyLength}/2000
                </span>
              </div>
              <Textarea
                id="feedback-body"
                rows={5}
                placeholder="Describe the issue or idea in detail…"
                disabled={isLoading}
                {...register("body")}
                className={cn("resize-none", errors.body && "border-destructive")}
              />
              {errors.body && (
                <p className="text-xs text-destructive">{errors.body.message}</p>
              )}
            </div>

            <div className="flex justify-end gap-2 pt-1">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleClose(false)}
                disabled={isLoading}
              >
                Cancel
              </Button>
              <Button type="submit" size="sm" disabled={isLoading}>
                {isLoading && <Loader2 className="h-4 w-4 animate-spin mr-1.5" />}
                Send Feedback
              </Button>
            </div>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
