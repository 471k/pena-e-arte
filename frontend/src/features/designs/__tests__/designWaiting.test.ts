import { describe, it, expect } from "vitest";
import { waitingLabel, waitingMessage } from "../designWaiting";

// E04c (A05): the server says whose move it is; these only word it for the viewer.

describe("waitingLabel", () => {
  it("is relative to the viewer", () => {
    expect(waitingLabel("Client", "client")).toBe("Waiting on you");
    expect(waitingLabel("Artist", "client")).toBe("Waiting on your artist");
    expect(waitingLabel("Artist", "artist")).toBe("Waiting on you");
    expect(waitingLabel("Client", "artist")).toBe("Waiting on client");
    expect(waitingLabel("Artist", "owner")).toBe("Waiting on artist");
    expect(waitingLabel("Client", "owner")).toBe("Waiting on client");
  });

  it("says nothing when nothing is outstanding", () => {
    for (const viewer of ["client", "artist", "owner"] as const) expect(waitingLabel("Nobody", viewer)).toBeNull();
  });
});

describe("waitingMessage", () => {
  const base = { latestVersionNumber: 3, latestUploadedAt: "2026-11-05T09:30:00Z" } as const;

  it("names the version and its upload time in the studio timezone", () => {
    expect(waitingMessage({ ...base, waitingOn: "Client", status: "InReview" }, "artist", null, "Europe/Tirane"))
      .toBe("Waiting on the client: version 3, uploaded 5 Nov 2026, 10:30 is with them for review.");
  });

  it("tells an expired review window apart from requested changes", () => {
    expect(waitingMessage({ ...base, waitingOn: "Artist", status: "ChangesRequested" }, "owner", "Expired", "UTC"))
      .toBe("The client didn't review version 3 in time. Upload a new revision, or ask them to review it.");
    expect(waitingMessage({ ...base, waitingOn: "Artist", status: "ChangesRequested" }, "owner", "ChangesRequested", "UTC"))
      .toBe("The client has requested changes to version 3. Upload a new revision to continue.");
  });

  it("with no version yet, says so", () => {
    const none = { waitingOn: "Artist", status: "Draft", latestVersionNumber: null, latestUploadedAt: null } as const;
    expect(waitingMessage(none, "artist", undefined, "UTC")).toBe("Waiting on the artist: no version has been uploaded yet.");
    expect(waitingMessage(none, "client", undefined, "UTC")).toBe("Waiting on your artist: they haven't uploaded a version yet.");
  });

  it("an approved design names the approved version", () => {
    expect(waitingMessage({ ...base, waitingOn: "Nobody", status: "Approved" }, "client", "Approved", "UTC"))
      .toBe("Version 3 is approved.");
  });
});
