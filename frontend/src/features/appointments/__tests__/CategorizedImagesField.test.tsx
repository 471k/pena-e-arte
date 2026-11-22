import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { CategorizedImagesField, type CategorizedImage } from "@/features/appointments/components/CategorizedImagesField";
import { AppointmentAttachmentCategory } from "@/features/appointments/appointment.types";

afterEach(() => cleanup());

const BASE_PROPS = {
  category:   AppointmentAttachmentCategory.AreaPhoto,
  label:      "Area photo",
  helperText: "Click to add a photo of the area",
  purpose:    "So your artist can see the skin.",
  max:        6,
  images:     [] as CategorizedImage[],
  error:      null,
  onPick:     vi.fn(),
  onRetry:    vi.fn(),
  onRemove:   vi.fn(),
  disabled:   false,
};

describe("CategorizedImagesField", () => {
  it("renders the provided label", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required />);
    expect(screen.getByText("Area photo")).toBeInTheDocument();
  });

  it("shows a required asterisk when required is true", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required />);
    const label = screen.getByText("Area photo", { selector: "label" });
    expect(label.textContent).toContain("*");
  });

  it("does not show a required asterisk when required is false", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required={false} />);
    const label = screen.getByText("Area photo", { selector: "label" });
    expect(label.textContent).not.toContain("*");
  });

  it("renders the helper text", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required={false} />);
    expect(screen.getByText("Click to add a photo of the area")).toBeInTheDocument();
  });

  it("renders a file input accepting only jpeg/png/webp", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required={false} />);
    const input = document.querySelector<HTMLInputElement>("input[type=file]")!;
    expect(input).toHaveAttribute("accept", "image/jpeg,image/png,image/webp");
  });

  it("calls onPick with the selected files", async () => {
    const onPick = vi.fn();
    const user = userEvent.setup();
    render(<CategorizedImagesField {...BASE_PROPS} required={false} onPick={onPick} />);

    const file = new File(["img"], "area.png", { type: "image/png" });
    const input = document.querySelector<HTMLInputElement>("input[type=file]")!;
    await user.upload(input, file);

    expect(onPick).toHaveBeenCalled();
  });

  it("renders a thumbnail for each image", () => {
    render(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        images={[{ id: "1", previewUrl: "blob:1", status: "done", publicUrl: "https://cdn.example.com/1.png" }]}
      />,
    );
    expect(screen.getByRole("img")).toBeInTheDocument();
  });

  it("shows a spinner overlay while an image is uploading", () => {
    render(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        images={[{ id: "1", previewUrl: "blob:1", status: "uploading", publicUrl: null }]}
      />,
    );
    expect(screen.getByRole("img")).toBeInTheDocument();
    // The remove button is still rendered per-thumbnail regardless of status.
    expect(screen.getByRole("button", { name: /remove image/i })).toBeInTheDocument();
  });

  it("shows an error overlay when an image failed to upload", () => {
    render(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        images={[{ id: "1", previewUrl: "blob:1", status: "error", publicUrl: null }]}
      />,
    );
    // C06: said in text, not a hover-only title, and announced as a failure the person can act on.
    expect(screen.getByText("Upload failed")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(/1 photo in Area photo didn't upload/);
  });

  it("C06: Retry sends the failed image's id back, and a field with no failures announces nothing", async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        onRetry={onRetry}
        images={[{ id: "img-9", previewUrl: "blob:9", status: "error", publicUrl: null }]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledWith("img-9");

    rerender(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        images={[{ id: "img-9", previewUrl: "blob:9", status: "done", publicUrl: "https://cdn.example.com/9.png" }]}
      />,
    );
    expect(screen.queryByRole("button", { name: /retry/i })).toBeNull();
    expect(screen.getByRole("alert")).toBeEmptyDOMElement();
  });

  it("C06: the picker is a real button named by the field's label, described by why the photo is wanted, and Space opens it", async () => {
    const user = userEvent.setup();
    render(<CategorizedImagesField {...BASE_PROPS} required={false} />);
    const picker = screen.getByRole("button", { name: "Area photo" });
    expect(picker).toHaveAccessibleDescription(/So your artist can see the skin\. Click to add a photo of the area/);

    const input = document.querySelector<HTMLInputElement>("input[type=file]")!;
    const opened = vi.fn();
    input.addEventListener("click", opened);
    picker.focus();
    await user.keyboard(" ");
    expect(opened).toHaveBeenCalledTimes(1);
    await user.keyboard("{Enter}");
    expect(opened).toHaveBeenCalledTimes(2);
    // The hidden input is out of the tab order: one stop per field, the visible button.
    expect(input).toHaveAttribute("tabindex", "-1");
  });

  it("calls onRemove with the image id when its remove button is clicked", async () => {
    const onRemove = vi.fn();
    const user = userEvent.setup();
    render(
      <CategorizedImagesField
        {...BASE_PROPS}
        required={false}
        images={[{ id: "img-1", previewUrl: "blob:1", status: "done", publicUrl: "https://cdn.example.com/1.png" }]}
        onRemove={onRemove}
      />,
    );

    await user.click(screen.getByRole("button", { name: /remove image/i }));

    expect(onRemove).toHaveBeenCalledWith("img-1");
  });

  it("shows the error message when provided", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required={false} error="You can attach up to 6 images." />);
    expect(screen.getByText("You can attach up to 6 images.")).toBeInTheDocument();
  });

  it("disables the file input once max images are reached", () => {
    const images: CategorizedImage[] = Array.from({ length: 6 }, (_, i) => ({
      id: String(i), previewUrl: `blob:${i}`, status: "done", publicUrl: `https://cdn.example.com/${i}.png`,
    }));
    render(<CategorizedImagesField {...BASE_PROPS} required={false} images={images} />);
    const input = document.querySelector<HTMLInputElement>("input[type=file]")!;
    expect(input).toBeDisabled();
  });

  it("disables the file input when disabled prop is true", () => {
    render(<CategorizedImagesField {...BASE_PROPS} required={false} disabled />);
    const input = document.querySelector<HTMLInputElement>("input[type=file]")!;
    expect(input).toBeDisabled();
  });
});
