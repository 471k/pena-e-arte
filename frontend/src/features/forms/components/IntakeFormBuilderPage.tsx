import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { ArrowLeft, ClipboardList, Loader2, Plus, Trash2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  useGetMyIntakeFormTemplateQuery,
  useUpsertIntakeFormTemplateMutation,
} from "../intakeFormsApi";
import type { IntakeFormFieldDefinition, IntakeFormFieldType } from "../form.types";

const FIELD_TYPES: { value: IntakeFormFieldType; label: string }[] = [
  { value: "Text",     label: "Short text" },
  { value: "Textarea", label: "Long text" },
  { value: "Select",   label: "Dropdown" },
  { value: "Checkbox", label: "Checkbox" },
  { value: "Date",     label: "Date" },
];

const MAX_FIELDS = 20;

function newField(): IntakeFormFieldDefinition {
  return { label: "", type: "Text", required: false };
}

export function IntakeFormBuilderPage() {
  useDocumentMeta({ title: "Intake Form Builder — TattooOS", canonical: "/intake-form-builder" });

  const navigate = useNavigate();
  const { data: template, isLoading, isError } = useGetMyIntakeFormTemplateQuery();
  const [upsertTemplate, { isLoading: isSaving }] = useUpsertIntakeFormTemplateMutation();

  const [fields, setFields] = useState<IntakeFormFieldDefinition[]>([]);
  const [isActive, setIsActive] = useState(true);
  // undefined = not yet initialized from the query result. Seeded during render (React's
  // documented "adjust state during render" escape hatch — see
  // https://react.dev/learn/you-might-not-need-an-effect) rather than in a useEffect, so it
  // runs exactly once when the query first resolves without a cascading-render lint violation.
  const [loadedTemplateId, setLoadedTemplateId] = useState<string | null | undefined>(undefined);
  const loaded = loadedTemplateId !== undefined;

  if (!isLoading && !loaded) {
    let initialFields: IntakeFormFieldDefinition[];
    if (template) {
      try {
        const parsed = JSON.parse(template.fieldSchemaJson) as IntakeFormFieldDefinition[];
        initialFields = Array.isArray(parsed) && parsed.length > 0 ? parsed : [newField()];
      } catch {
        initialFields = [newField()];
      }
    } else {
      initialFields = [newField()];
    }
    setFields(initialFields);
    setIsActive(template?.isActive ?? true);
    setLoadedTemplateId(template?.id ?? null);
  }

  function updateField(index: number, patch: Partial<IntakeFormFieldDefinition>) {
    setFields((prev) => prev.map((f, i) => (i === index ? { ...f, ...patch } : f)));
  }

  function removeField(index: number) {
    setFields((prev) => prev.filter((_, i) => i !== index));
  }

  function addField() {
    if (fields.length >= MAX_FIELDS) return;
    setFields((prev) => [...prev, newField()]);
  }

  const hasEmptyLabel = fields.some((f) => !f.label.trim());
  const canSave = fields.length > 0 && !hasEmptyLabel;

  async function handleSave() {
    if (!canSave) {
      toast.error("Every field needs a label.");
      return;
    }
    const result = await upsertTemplate({
      fieldSchemaJson: JSON.stringify(fields),
      isActive,
    });
    if ("data" in result) {
      toast.success("Intake form saved.");
    } else {
      toast.error("Failed to save intake form.");
    }
  }

  if (isLoading || !loaded) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading intake form builder">
        <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-32" />
        </header>
        <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-md" />
          ))}
        </main>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive-text">Failed to load your intake form.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)} className="gap-1.5">
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div className="flex items-center gap-2">
          <ClipboardList className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Intake Form Builder</span>
        </div>
        <div className="w-16" />
      </header>

      <main className="max-w-lg mx-auto px-4 py-6 space-y-5">
        <p className="text-sm text-muted-foreground">
          Customize the fields clients fill out on their intake form. Leave this inactive (or with
          no fields configured) to keep the default single medical-history textarea.
        </p>

        <div className="space-y-3">
          {fields.map((field, index) => (
            <div key={index} className="rounded-md border p-3 space-y-2">
              <div className="flex items-start gap-2">
                <div className="flex-1 space-y-1.5">
                  <Label htmlFor={`field-label-${index}`}>Label</Label>
                  <Input
                    id={`field-label-${index}`}
                    value={field.label}
                    onChange={(e) => updateField(index, { label: e.target.value })}
                    placeholder="e.g. Allergies"
                  />
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="mt-6 text-destructive-text hover:text-destructive-text"
                  onClick={() => removeField(index)}
                  aria-label={`Remove field ${index + 1}`}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              </div>

              <div className="flex items-center gap-3">
                <div className="flex-1 space-y-1.5">
                  <Label htmlFor={`field-type-${index}`}>Type</Label>
                  <Select
                    value={field.type}
                    onValueChange={(v) => updateField(index, { type: v as IntakeFormFieldType })}
                  >
                    <SelectTrigger id={`field-type-${index}`}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {FIELD_TYPES.map((t) => (
                        <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <label className="flex items-center gap-1.5 cursor-pointer text-sm mt-5">
                  <input
                    type="checkbox"
                    checked={field.required}
                    onChange={(e) => updateField(index, { required: e.target.checked })}
                    className="h-4 w-4 rounded border-input accent-primary"
                  />
                  Required
                </label>
              </div>

              {field.type === "Select" && (
                <div className="space-y-1.5">
                  <Label htmlFor={`field-options-${index}`}>Options (one per line)</Label>
                  <Textarea
                    id={`field-options-${index}`}
                    rows={3}
                    value={(field.options ?? []).join("\n")}
                    onChange={(e) => updateField(index, {
                      options: e.target.value.split("\n").map((o) => o.trim()).filter(Boolean),
                    })}
                    className="resize-none"
                  />
                </div>
              )}
            </div>
          ))}
        </div>

        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={addField}
          disabled={fields.length >= MAX_FIELDS}
          className="gap-1.5"
        >
          <Plus className="h-3.5 w-3.5" />
          Add field
        </Button>

        <label className="flex items-center gap-2 cursor-pointer">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-input accent-primary"
          />
          <span className="text-sm">Active — use these fields instead of the default textarea</span>
        </label>

        <Button className="w-full" disabled={isSaving || !canSave} onClick={handleSave}>
          {isSaving ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Saving…
            </>
          ) : (
            "Save Intake Form"
          )}
        </Button>
      </main>
    </div>
  );
}
