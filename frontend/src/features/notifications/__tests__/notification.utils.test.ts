import { describe, it, expect } from "vitest";
import { stripHtml } from "../notification.utils";

describe("stripHtml", () => {
  it("removes tags and collapses whitespace", () => {
    expect(stripHtml("<p>Hello   <strong>there</strong></p>\n<p>friend</p>")).toBe("Hello there friend");
  });

  it("drops the <title> text so it does not duplicate the heading", () => {
    const email =
      '<!DOCTYPE html><html><head><meta charset="UTF-8"><title>New Booking Request</title></head>' +
      "<body><h2>New booking request</h2><p><strong>Ana Costa</strong> has submitted a booking request.</p></body></html>";

    expect(stripHtml(email)).toBe("New booking request Ana Costa has submitted a booking request.");
  });

  it("drops <style> and <script> content", () => {
    expect(stripHtml("<style>p{color:red}</style><p>Visible</p><script>alert(1)</script>")).toBe("Visible");
  });

  it("decodes common HTML entities and leaves unknown ones alone", () => {
    expect(stripHtml("<p>Ink &amp; Soul &nbsp;&lt;3 &quot;quoted&quot; it&#39;s &copy;</p>"))
      .toBe('Ink & Soul <3 "quoted" it\'s &copy;');
  });

  it("returns plain text unchanged", () => {
    expect(stripHtml("Your appointment is tomorrow.")).toBe("Your appointment is tomorrow.");
  });
});
