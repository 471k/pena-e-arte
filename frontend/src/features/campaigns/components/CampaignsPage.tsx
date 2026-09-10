import { useState } from "react";
import { Megaphone, Send } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Badge } from "@/shared/components/ui/badge";
import { Alert, AlertDescription, AlertTitle } from "@/shared/components/ui/alert";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/components/ui/table";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPlansQuery, useGetSubscriptionQuery } from "@/features/billing/billingApi";
import {
  useGetCampaignsQuery, useCreateCampaignMutation, useSendCampaignMutation,
} from "@/features/campaigns/campaignsApi";
import type { CampaignAudience } from "@/features/campaigns/campaigns.types";

export function CampaignsPage() {
  useDocumentMeta({ title: "Marketing Campaigns — TattooOS", canonical: "/campaigns" });

  const { data: subscription } = useGetSubscriptionQuery();
  const { data: plans } = useGetPlansQuery();
  const currentPlan = plans?.find((p) => p.id === subscription?.planId);
  const allowed = currentPlan?.allowMarketingCampaigns ?? false;

  const { data: campaigns, isLoading } = useGetCampaignsQuery(undefined, { skip: !allowed });
  const [createCampaign, { isLoading: creating }] = useCreateCampaignMutation();
  const [sendCampaign, { isLoading: sending }] = useSendCampaignMutation();

  const [subject, setSubject] = useState("");
  const [bodyHtml, setBodyHtml] = useState("");
  const [audience, setAudience] = useState<CampaignAudience>("AllClients");
  const [noRecentVisitDays, setNoRecentVisitDays] = useState(90);

  async function handleCreate() {
    if (!subject.trim() || !bodyHtml.trim()) {
      toast.error("Subject and message body are required.");
      return;
    }
    const result = await createCampaign({
      subject,
      bodyHtml,
      audience,
      ...(audience === "ClientsWithNoRecentVisit" ? { noRecentVisitDays } : {}),
    });
    if ("data" in result) {
      toast.success("Draft saved.");
      setSubject("");
      setBodyHtml("");
    } else {
      toast.error("Failed to save draft.");
    }
  }

  async function handleSend(id: string) {
    const result = await sendCampaign(id);
    if ("data" in result) {
      toast.success("Campaign is sending.");
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to send campaign.";
      toast.error(errMsg);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Megaphone className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Marketing Campaigns</span>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-6">
        {!allowed ? (
          <Alert>
            <Megaphone className="h-4 w-4" />
            <AlertTitle>Not included on your plan</AlertTitle>
            <AlertDescription>
              Marketing email campaigns aren't included on your current plan. Upgrade to send
              campaigns to your clients.
            </AlertDescription>
          </Alert>
        ) : (
          <>
            <Card>
              <CardHeader>
                <CardTitle>New campaign</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-1.5">
                  <label htmlFor="campaignSubject" className="text-sm font-medium">Subject</label>
                  <Input
                    id="campaignSubject"
                    value={subject}
                    onChange={(e) => setSubject(e.target.value)}
                    maxLength={200}
                  />
                </div>
                <div className="space-y-1.5">
                  <label htmlFor="campaignBody" className="text-sm font-medium">Message</label>
                  <Textarea
                    id="campaignBody"
                    rows={6}
                    value={bodyHtml}
                    onChange={(e) => setBodyHtml(e.target.value)}
                    placeholder="HTML or plain text — an unsubscribe link is added automatically."
                  />
                </div>
                <div className="space-y-1.5">
                  <label htmlFor="campaignAudience" className="text-sm font-medium">Audience</label>
                  <Select value={audience} onValueChange={(v) => setAudience(v as CampaignAudience)}>
                    <SelectTrigger id="campaignAudience">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="AllClients">All clients (opted in)</SelectItem>
                      <SelectItem value="ClientsWithNoRecentVisit">No recent visit</SelectItem>
                    </SelectContent>
                  </Select>
                  <p className="text-xs text-muted-foreground">
                    Only clients who opted in to marketing email ever receive a campaign.
                  </p>
                </div>
                {audience === "ClientsWithNoRecentVisit" && (
                  <div className="space-y-1.5">
                    <label htmlFor="noRecentVisitDays" className="text-sm font-medium">
                      No completed appointment in the last
                    </label>
                    <div className="flex items-center gap-2">
                      <Input
                        id="noRecentVisitDays"
                        type="number"
                        min={1}
                        className="w-24"
                        value={noRecentVisitDays}
                        onChange={(e) => setNoRecentVisitDays(Number(e.target.value))}
                      />
                      <span className="text-sm text-muted-foreground">days</span>
                    </div>
                  </div>
                )}
                <Button onClick={() => void handleCreate()} disabled={creating}>
                  Save draft
                </Button>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Send history</CardTitle>
              </CardHeader>
              <CardContent>
                {isLoading && (
                  <div className="space-y-2">
                    <Skeleton className="h-10 w-full" />
                    <Skeleton className="h-10 w-full" />
                  </div>
                )}
                {campaigns && campaigns.length === 0 && (
                  <p className="text-sm text-muted-foreground">No campaigns yet.</p>
                )}
                {campaigns && campaigns.length > 0 && (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Subject</TableHead>
                        <TableHead>Status</TableHead>
                        <TableHead>Delivered</TableHead>
                        <TableHead />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {campaigns.map((c) => (
                        <TableRow key={c.id}>
                          <TableCell>{c.subject}</TableCell>
                          <TableCell>
                            <Badge variant={c.status === "Sent" ? "default" : "secondary"}>
                              {c.status}
                            </Badge>
                          </TableCell>
                          <TableCell>
                            {c.status === "Draft" ? "—" : `${c.deliveredCount}/${c.recipientCount}`}
                          </TableCell>
                          <TableCell>
                            {c.status === "Draft" && (
                              <Button
                                size="sm"
                                variant="secondary"
                                className="gap-1.5"
                                onClick={() => void handleSend(c.id)}
                                disabled={sending}
                              >
                                <Send className="h-3.5 w-3.5" />
                                Send
                              </Button>
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </CardContent>
            </Card>
          </>
        )}
      </main>
    </div>
  );
}
