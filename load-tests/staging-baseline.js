// Baseline load test — public booking-request write path + public discover/portfolio
// browse read path. See docs/infra/load-test-baseline-2026-09-03.md for context, tool
// choice rationale (k6 over Artillery), and results from the first real run.
//
// STAGING ONLY. Never point BASE_URL at production.
//
// Run:
//   k6 run load-tests/staging-baseline.js
//   k6 run -e BASE_URL=https://staging.tattooos.co load-tests/staging-baseline.js
//
// Requires a studio with at least one active artist to exist at STUDIO_SLUG (see the
// docs file for how "load-test-studio" was seeded on staging via the real registration
// API — no direct DB writes).

import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

const BASE_URL = __ENV.BASE_URL || "https://staging.tattooos.co";
const STUDIO_SLUG = __ENV.STUDIO_SLUG || "load-test-studio";
// Prefix-only check server-side (IR2Service.IsR2Url just matches this prefix) — these
// URLs don't need to point at real uploaded objects for the booking write path itself.
const FAKE_R2_BASE = "https://pena-e-arte-r2-staging.phisoftwaresolutions.workers.dev/load-test";

const bookingErrorRate = new Rate("booking_errors");
const browseErrorRate = new Rate("browse_errors");
const bookingDuration = new Trend("booking_duration", true);
const browseDuration = new Trend("browse_duration", true);

// A modest, realistic spike, not a stress-to-failure test — first baseline for this app.
export const options = {
  scenarios: {
    guest_booking: {
      executor: "ramping-vus",
      exec: "guestBooking",
      startVUs: 0,
      stages: [
        { duration: "2m", target: 25 },
        { duration: "3m", target: 25 },
        { duration: "1m", target: 0 },
      ],
    },
    discover_browse: {
      executor: "ramping-vus",
      exec: "discoverBrowse",
      startVUs: 0,
      stages: [
        { duration: "2m", target: 25 },
        { duration: "3m", target: 25 },
        { duration: "1m", target: 0 },
      ],
    },
  },
  thresholds: {
    booking_errors: ["rate<0.05"],
    browse_errors: ["rate<0.05"],
    "booking_duration": ["p(95)<2000"],
    "browse_duration": ["p(95)<1000"],
  },
};

// Randomized per iteration on purpose: the seeded load-test studio has exactly one
// artist, so every concurrent VU booking the *same* slot would collide on the real
// conflict check after the first success — measuring "slot already booked" 422s, not
// the real write path. Spreading across a wide future window (1-60 days, 9-20h, on
// the hour) keeps collisions rare relative to the ~25 concurrent VUs this scenario runs.
function randomFutureDateIso() {
  const daysAhead = 1 + Math.floor(Math.random() * 60);
  const hour = 9 + Math.floor(Math.random() * 11); // 09:00-19:00
  const d = new Date(Date.now() + daysAhead * 86_400_000);
  d.setUTCHours(hour, 0, 0, 0);
  return d.toISOString();
}

export function guestBooking() {
  const uniqueId = `${__VU}-${__ITER}-${Date.now()}`;
  const payload = JSON.stringify({
    firstName: "Load",
    lastName: "Test",
    email: `loadtest-${uniqueId}@example.com`,
    phone: "+355692345678",
    marketingOptIn: false,
    booking: {
      artistId: null, // bookAnyArtist path — matches "let the studio choose" real usage
      clientId: "00000000-0000-0000-0000-000000000000",
      date: randomFutureDateIso(),
      durationMinutes: 60,
      notes: "k6 load-test booking — safe to ignore/delete",
      tattooDescription: "Load test — small placeholder tattoo description",
      safetyNotes: null,
      desiredPlacementLocations: ["Forearm"],
      referralSource: "Other",
      referralSourceOther: "k6 load test",
      images: [
        { url: `${FAKE_R2_BASE}/${uniqueId}/area-photo.jpg`, category: "AreaPhoto" },
        { url: `${FAKE_R2_BASE}/${uniqueId}/reference.jpg`, category: "Reference" },
      ],
    },
  });

  const res = http.post(`${BASE_URL}/api/v1/public/studios/${STUDIO_SLUG}/book`, payload, {
    headers: { "Content-Type": "application/json" },
    tags: { name: "guest_booking" },
  });

  bookingDuration.add(res.timings.duration);
  const ok = check(res, { "booking status is 2xx": (r) => r.status >= 200 && r.status < 300 });
  bookingErrorRate.add(!ok);

  sleep(1);
}

export function discoverBrowse() {
  // Tirana coordinates — matches the seeded studio's location, so results are non-empty.
  const nearbyRes = http.get(
    `${BASE_URL}/api/v1/public/studios/nearby?lat=41.3275&lng=19.8187&radiusKm=50`,
    { tags: { name: "discover_nearby" } },
  );
  browseDuration.add(nearbyRes.timings.duration);
  let ok = check(nearbyRes, { "nearby status is 200": (r) => r.status === 200 });
  browseErrorRate.add(!ok);

  const profileRes = http.get(
    `${BASE_URL}/api/v1/public/studios/${STUDIO_SLUG}`,
    { tags: { name: "discover_studio_profile" } },
  );
  browseDuration.add(profileRes.timings.duration);
  ok = check(profileRes, { "studio profile status is 200": (r) => r.status === 200 });
  browseErrorRate.add(!ok);

  sleep(1);
}
