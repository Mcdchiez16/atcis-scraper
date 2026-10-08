import { createClient } from "npm:@supabase/supabase-js@2";

type TenderRecord = {
  id?: string;
  source_id?: string;
  country?: string;
  status?: string;
  scraped_at?: string;
  payload?: Record<string, unknown>;
};

type DatabaseWebhookPayload = {
  type?: string;
  table?: string;
  schema?: string;
  record?: TenderRecord | null;
  recipient?: string;
};

type NotificationPreference = {
  user_id: string;
  email: string;
  enabled: boolean;
  email_enabled: boolean;
  country_scope: "ALL" | "ZW" | "ZM";
  alert_types: string[];
  preferences?: {
    categories?: unknown;
    keywords?: unknown;
    minimumMatchScore?: unknown;
  };
};

type UserProfile = {
  id: string;
  country: "ALL" | "ZW" | "ZM";
  active: boolean;
};

type DeliveryResult = "sent" | "duplicate" | "failed";

const CATEGORY_TERMS: Record<string, string[]> = {
  "ict-software": [
    "ict",
    "information technology",
    "software",
    "cloud",
    "telecom",
    "computer",
    "laptop",
    "server",
    "network",
    "cybersecurity",
    "digital",
    "database",
    "printer",
  ],
  "construction-infrastructure": [
    "construction",
    "infrastructure",
    "civil works",
    "building",
    "road",
    "bridge",
    "water works",
    "engineering",
    "rehabilitation",
  ],
  "goods-supplies": [
    "goods",
    "supplies",
    "supply",
    "equipment",
    "consumables",
    "furniture",
    "stationery",
    "materials",
  ],
  "transport-logistics": [
    "transport",
    "logistics",
    "vehicle",
    "fleet",
    "freight",
    "warehouse",
    "warehousing",
  ],
  "energy-utilities": [
    "energy",
    "solar",
    "electrical",
    "electricity",
    "power generation",
    "utility",
    "generator",
  ],
  "agriculture-food": [
    "agriculture",
    "agricultural",
    "farming",
    "seed",
    "fertilizer",
    "irrigation",
    "food",
    "livestock",
    "agribusiness",
  ],
  "professional-services": [
    "consulting",
    "consultancy",
    "audit",
    "training",
    "research",
    "advisory",
    "professional services",
  ],
  "healthcare-medical": [
    "health",
    "healthcare",
    "medical",
    "hospital",
    "pharmaceutical",
    "medicine",
    "laboratory",
    "clinic",
  ],
};

const CATEGORY_SECTORS: Record<string, string> = {
  "ict-software": "ICT & Software",
  "construction-infrastructure": "Civil & Infrastructure",
  "goods-supplies": "General Goods & Consumables",
  "energy-utilities": "Electrical & Energy",
  "healthcare-medical": "Healthcare & Medical",
};

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function normalizeText(value: unknown): string {
  return String(value ?? "")
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}

function textValues(value: unknown): string[] {
  if (Array.isArray(value)) return value.flatMap(textValues);
  if (typeof value === "string" || typeof value === "number") return [String(value)];
  return [];
}

function payloadValue(payload: Record<string, unknown>, ...keys: string[]): unknown {
  for (const key of keys) {
    if (payload[key] !== undefined && payload[key] !== null) return payload[key];
  }
  return "";
}

function lineItemText(payload: Record<string, unknown>): string[] {
  const lineItems = payloadValue(payload, "lineItems", "LineItems");
  if (!Array.isArray(lineItems)) return [];
  return lineItems.flatMap((item) => {
    if (!item || typeof item !== "object") return [];
    const record = item as Record<string, unknown>;
    return textValues([
      payloadValue(record, "description", "Description"),
      payloadValue(record, "specification", "Specification"),
    ]);
  });
}

function tenderSearchText(tender: TenderRecord): string {
  const payload = tender.payload ?? {};
  return normalizeText(
    [
      payloadValue(payload, "title", "Title"),
      payloadValue(payload, "description", "Description"),
      payloadValue(payload, "scope", "Scope"),
      payloadValue(payload, "commodityGroup", "CommodityGroup"),
      payloadValue(payload, "procurementType", "ProcurementType"),
      payloadValue(payload, "procuringEntity", "ProcuringEntity"),
      ...lineItemText(payload),
    ]
      .flatMap(textValues)
      .join(" "),
  );
}

function isSearchMatch(searchText: string, term: string): boolean {
  const normalizedTerm = normalizeText(term);
  return normalizedTerm.length > 0 && ` ${searchText} `.includes(` ${normalizedTerm} `);
}

function matchesTenderTopics(preference: NotificationPreference, tender: TenderRecord): boolean {
  const rawCategories = preference.preferences?.categories;
  const rawKeywords = preference.preferences?.keywords;
  const categories = Array.isArray(rawCategories)
    ? rawCategories.filter((value): value is string => typeof value === "string")
    : [];
  const keywords = Array.isArray(rawKeywords)
    ? rawKeywords.filter((value): value is string => typeof value === "string")
    : [];
  if (categories.length === 0 && keywords.length === 0) return false;

  const searchText = tenderSearchText(tender);
  const payload = tender.payload ?? {};
  const hasCanonicalClassification = payload.classificationVersion === "tender-rules-v2";
  const hasReviewedClassification =
    hasCanonicalClassification && payload.needsClassificationReview === false;
  const classifiedSector = hasReviewedClassification ? String(payload.sector || "") : "";
  const categoryMatch = categories.some((category) => {
    if (hasCanonicalClassification && !hasReviewedClassification) return false;
    if (!hasReviewedClassification) {
      return (CATEGORY_TERMS[category] ?? []).some((term) => isSearchMatch(searchText, term));
    }
    const directSector = CATEGORY_SECTORS[category];
    if (directSector) return classifiedSector === directSector;
    if (category === "transport-logistics" && classifiedSector !== "Services & Logistics") return false;
    if (category === "professional-services" && classifiedSector !== "Services & Logistics") return false;
    if (category === "agriculture-food" && classifiedSector !== "General Goods & Consumables") return false;
    return (CATEGORY_TERMS[category] ?? []).some((term) => isSearchMatch(searchText, term));
  });
  return categoryMatch || keywords.some((keyword) => isSearchMatch(searchText, keyword));
}

function matchesMinimumScore(preference: NotificationPreference, tender: TenderRecord): boolean {
  const rawMinimum = Number(preference.preferences?.minimumMatchScore ?? 70);
  const minimum = [50, 70, 85, 95].includes(rawMinimum) ? rawMinimum : 70;
  const payload = tender.payload ?? {};
  const rawScore = payloadValue(payload, "aiScore", "AiScore", "matchScore", "MatchScore");
  const score = typeof rawScore === "string" ? Number(rawScore.replace("%", "").trim()) : Number(rawScore);
  return Number.isFinite(score) && score >= minimum;
}

function countryCode(value: unknown): "ALL" | "ZW" | "ZM" | null {
  const normalized = normalizeText(value);
  if (normalized === "all") return "ALL";
  if (normalized === "zw" || normalized === "zimbabwe") return "ZW";
  if (normalized === "zm" || normalized === "zambia") return "ZM";
  return null;
}

function matchesTenderCountry(
  preference: NotificationPreference,
  profile: UserProfile | undefined,
  tender: TenderRecord,
): boolean {
  if (!profile?.active) return false;
  const tenderCountry = countryCode(tender.country);
  const profileCountry = countryCode(profile.country);
  const preferenceCountry = countryCode(preference.country_scope);
  if (!tenderCountry || !profileCountry || !preferenceCountry || tenderCountry === "ALL") return false;

  // A country-specific user can never broaden their email scope in preferences.
  const effectiveCountry = profileCountry === "ALL" ? preferenceCountry : profileCountry;
  return effectiveCountry === "ALL" || effectiveCountry === tenderCountry;
}

function tenderIdentity(tender: TenderRecord): string {
  return String(tender.id || tender.source_id || "").trim();
}

function tenderPeriod(tender: TenderRecord): string {
  const payload = tender.payload ?? {};
  const rawDate = String(
    payloadValue(payload, "closingDate", "ClosingDate", "publishDate", "PublishDate") || tender.scraped_at || "",
  );
  const match = rawDate.match(/\b(\d{4})-(\d{2})/);
  return match ? `${match[1]}-${match[2]}` : new Date().toISOString().slice(0, 7);
}

async function tenderDedupeKey(tender: TenderRecord): Promise<string> {
  const payload = tender.payload ?? {};
  const title = normalizeText(payloadValue(payload, "title", "Title")) || normalizeText(tenderIdentity(tender));
  const entity = normalizeText(payloadValue(payload, "procuringEntity", "ProcuringEntity"));
  const input = [countryCode(tender.country), title, entity, tenderPeriod(tender)].join("|");
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(input));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function escapeHtml(value: unknown): string {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function adminClient() {
  const url = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !serviceKey) throw new Error("Supabase server configuration is incomplete");
  return createClient(url, serviceKey, { auth: { persistSession: false, autoRefreshToken: false } });
}

async function deliverTenderOnce(
  admin: ReturnType<typeof adminClient>,
  preference: NotificationPreference,
  tender: TenderRecord,
  dedupeKey: string,
  subject: string,
  html: string,
): Promise<DeliveryResult> {
  const tenderId = tenderIdentity(tender);
  if (!tenderId) throw new Error("Tender identity is missing");

  const { data: claim, error: claimError } = await admin
    .from("tender_notification_deliveries")
    .insert({
      user_id: preference.user_id,
      tender_id: tenderId,
      dedupe_key: dedupeKey,
      alert_type: "new-tender",
      recipient_email: preference.email,
    })
    .select("id")
    .single();

  if (claimError?.code === "23505") return "duplicate";
  if (claimError) throw claimError;

  try {
    const emailId = await sendEmail(preference.email, subject, html);
    const { error: updateError } = await admin
      .from("tender_notification_deliveries")
      .update({ status: "sent", provider_message_id: emailId, sent_at: new Date().toISOString() })
      .eq("id", claim.id);
    // Keep the claim even if recording the provider response fails. The email
    // was accepted, so deleting it here could cause a duplicate delivery.
    if (updateError) console.error("Failed to finalize tender delivery claim", updateError);
    return "sent";
  } catch (error) {
    // Resend did not accept the email, so release the claim for a safe retry.
    const { error: releaseError } = await admin
      .from("tender_notification_deliveries")
      .delete()
      .eq("id", claim.id)
      .eq("status", "pending");
    if (releaseError) console.error("Failed to release tender delivery claim", releaseError);
    console.error("Tender email delivery failed", error);
    return "failed";
  }
}

async function authenticatedUserId(request: Request) {
  const authorization = request.headers.get("Authorization");
  const token = authorization?.replace(/^Bearer\s+/i, "").trim();
  const url = Deno.env.get("SUPABASE_URL");
  const anonKey = Deno.env.get("SUPABASE_ANON_KEY");
  if (!token || !url || !anonKey) return null;
  const client = createClient(url, anonKey, { auth: { persistSession: false, autoRefreshToken: false } });
  const { data, error } = await client.auth.getUser(token);
  return error ? null : data.user?.id ?? null;
}

function isWebhookRequest(request: Request) {
  const expected = Deno.env.get("TENDER_WEBHOOK_SECRET");
  const received = request.headers.get("x-atcis-webhook-secret");
  return Boolean(expected && received && expected === received);
}

async function sendEmail(to: string, subject: string, html: string) {
  const resendApiKey = Deno.env.get("RESEND_API_KEY");
  const fromEmail = Deno.env.get("RESEND_FROM_EMAIL");
  if (!resendApiKey || !fromEmail) throw new Error("Email configuration is incomplete");

  const response = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${resendApiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ from: fromEmail, to: [to], subject, html }),
  });
  const result = await response.json();
  if (!response.ok) {
    console.error("Resend rejected tender email", result);
    throw new Error("Email delivery failed");
  }
  return result.id as string;
}

function tenderEmail(tender: TenderRecord) {
  const payload = tender.payload ?? {};
  const title = payload.title || "New tender opportunity";
  const entity = payload.procuringEntity || "Not specified";
  const closingDate = payload.closingDate || "Not specified";
  const sourceUrl = payload.sourceUrl || "";
  const country = tender.country || "Not specified";
  const reference = tender.source_id || tender.id || "Not specified";
  const sourceLink = sourceUrl
    ? `<p><a href="${escapeHtml(sourceUrl)}">View tender details</a></p>`
    : "";

  return {
    subject: `New tender: ${String(title)}`,
    html: `
      <h2>New tender opportunity</h2>
      <p><strong>Title:</strong> ${escapeHtml(title)}</p>
      <p><strong>Organisation:</strong> ${escapeHtml(entity)}</p>
      <p><strong>Reference:</strong> ${escapeHtml(reference)}</p>
      <p><strong>Country:</strong> ${escapeHtml(country)}</p>
      <p><strong>Closing date:</strong> ${escapeHtml(closingDate)}</p>
      ${sourceLink}
      <p style="color:#64748b;font-size:12px">You received this because tender email alerts are enabled in your ATCIS notification preferences.</p>
    `,
  };
}

Deno.serve(async (request) => {
  try {
    if (request.method !== "POST") {
      return Response.json({ error: "Method not allowed" }, { status: 405 });
    }

    let event: DatabaseWebhookPayload;
    try {
      event = await request.json();
    } catch {
      return Response.json({ error: "Invalid JSON payload" }, { status: 400 });
    }

    const webhookRequest = isWebhookRequest(request);
    const userId = webhookRequest ? null : await authenticatedUserId(request);
    const admin = adminClient();

    if (event.type === "TEST") {
      let recipient = "";
      if (userId) {
        const { data, error } = await admin
          .from("notification_preferences")
          .select("email,enabled,email_enabled")
          .eq("user_id", userId)
          .maybeSingle();
        if (error) throw error;
        if (!data?.enabled || !data.email_enabled) {
          return Response.json(
            { error: "Turn on alerts and the email channel, then save before testing." },
            { status: 400 },
          );
        }
        recipient = String(data.email || "");
      } else if (webhookRequest) {
        recipient = String(event.recipient || "").trim().toLowerCase();
      } else {
        return Response.json({ error: "Authentication required" }, { status: 401 });
      }

      if (!EMAIL_PATTERN.test(recipient)) {
        return Response.json({ error: "A valid test recipient is required" }, { status: 400 });
      }
      const emailId = await sendEmail(
        recipient,
        "ATCIS tender alerts are working",
        "<h2>Test successful</h2><p>Your ATCIS tender email notifications are configured correctly.</p>",
      );
      return Response.json({ sent: true, emailId });
    }

    if (!webhookRequest) {
      return Response.json({ error: "Webhook authentication required" }, { status: 403 });
    }

    const tender = event.record;
    if (
      event.type !== "INSERT" ||
      event.schema !== "public" ||
      event.table !== "tenders" ||
      !tender ||
      tender.status !== "live"
    ) {
      return Response.json({ skipped: true });
    }

    const { data, error } = await admin
      .from("notification_preferences")
      .select("user_id,email,enabled,email_enabled,country_scope,alert_types,preferences")
      .eq("enabled", true)
      .eq("email_enabled", true)
      .contains("alert_types", ["new-tender"]);
    if (error) throw error;

    const preferences = (data ?? []) as NotificationPreference[];
    const userIds = preferences.map((preference) => preference.user_id);
    const profilesById = new Map<string, UserProfile>();
    if (userIds.length > 0) {
      const { data: profiles, error: profileError } = await admin
        .from("profiles")
        .select("id,country,active")
        .in("id", userIds);
      if (profileError) throw profileError;
      for (const profile of (profiles ?? []) as UserProfile[]) profilesById.set(profile.id, profile);
    }

    const recipients = preferences.filter((preference) => {
      const profile = profilesById.get(preference.user_id);
      return (
        EMAIL_PATTERN.test(preference.email) &&
        matchesTenderCountry(preference, profile, tender) &&
        matchesTenderTopics(preference, tender) &&
        matchesMinimumScore(preference, tender)
      );
    });
    if (recipients.length === 0) {
      return Response.json({ sent: 0, message: "No enabled recipients matched this tender" });
    }

    const content = tenderEmail(tender);
    const dedupeKey = await tenderDedupeKey(tender);
    const results = await Promise.all(
      recipients.map((preference) =>
        deliverTenderOnce(admin, preference, tender, dedupeKey, content.subject, content.html),
      ),
    );
    const sent = results.filter((result) => result === "sent").length;
    const duplicates = results.filter((result) => result === "duplicate").length;
    const failed = results.filter((result) => result === "failed").length;
    if (failed > 0) {
      return Response.json(
        { sent, duplicates, failed, error: "Some emails could not be delivered" },
        { status: 502 },
      );
    }
    return Response.json({ sent, duplicates, failed: 0 });
  } catch (error) {
    console.error("Tender alert function failed", error);
    const message = error instanceof Error ? error.message : "Tender alert function failed";
    return Response.json({ error: message }, { status: 500 });
  }
});
