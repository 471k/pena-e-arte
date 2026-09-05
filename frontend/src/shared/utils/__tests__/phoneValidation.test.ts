import { describe, it, expect } from "vitest";
import { isValidE164Phone } from "../phoneValidation";

describe("isValidE164Phone", () => {
  it("treats empty string as valid", () => {
    expect(isValidE164Phone("")).toBe(true);
  });

  it("treats null as valid", () => {
    expect(isValidE164Phone(null)).toBe(true);
  });

  it("treats undefined as valid", () => {
    expect(isValidE164Phone(undefined)).toBe(true);
  });

  it("accepts a valid E.164 number", () => {
    expect(isValidE164Phone("+351912345678")).toBe(true);
  });

  it("rejects a national-format number with no country code", () => {
    expect(isValidE164Phone("912345678")).toBe(false);
  });

  it("rejects a number too short to be real", () => {
    expect(isValidE164Phone("+35191234")).toBe(false);
  });

  it("rejects a non-phone string", () => {
    expect(isValidE164Phone("not a phone number")).toBe(false);
  });
});
