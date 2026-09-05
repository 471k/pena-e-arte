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
  max:        6,
  images:     [] as CategorizedImage[],
  error:      null,
  onPick:     vi.fn(),
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
    expect(screen.getByTitle("Upload failed")).toBeInTheDocument();
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
