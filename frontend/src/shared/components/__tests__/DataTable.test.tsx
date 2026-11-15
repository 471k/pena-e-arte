import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useParams } from "react-router-dom";
import { createPortal } from "react-dom";

import { DataTable, type ColumnDef } from "@/shared/components/DataTable";
import { RecordLink } from "@/shared/components/RecordLink";

interface Row {
  id:   string;
  name: string;
  city: string;
}

const COLUMNS: ColumnDef<Row>[] = [
  { header: "Name", accessorKey: "name" },
  { header: "City", accessorKey: "city" },
];

const DATA: Row[] = [
  { id: "r1", name: "Alice", city: "Porto"  },
  { id: "r2", name: "Bob",   city: "Lisbon" },
];

function Opened() {
  const { id } = useParams();
  return <p>Opened {id}</p>;
}

function renderInRouter(ui: React.ReactNode) {
  render(
    <MemoryRouter initialEntries={["/rows"]}>
      <Routes>
        <Route path="/rows" element={ui} />
        <Route path="/rows/:id" element={<Opened />} />
      </Routes>
    </MemoryRouter>,
  );
}

function renderTable(
  data: Row[] = DATA,
  rowHref?: (row: Row) => string,
  columns: ColumnDef<Row>[] = COLUMNS,
) {
  renderInRouter(
    <DataTable
      columns={columns}
      data={data}
      keyExtractor={(row) => row.id}
      rowHref={rowHref}
    />,
  );
}

// The S03 shape every consumer follows: the title is a RecordLink, actions are sibling buttons.
const LINKED_COLUMNS = (onAction: () => void): ColumnDef<Row>[] => [
  { header: "Name", cell: (row) => <RecordLink to={`/rows/${row.id}`}>{row.name}</RecordLink> },
  { header: "City", accessorKey: "city" },
  { header: "", cell: (row) => <button type="button" onClick={onAction}>Edit {row.name}</button> },
];

describe("DataTable", () => {
  it("renders column headers", () => {
    renderTable();
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("City")).toBeInTheDocument();
  });

  it("renders a row for each data item", () => {
    renderTable();
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("Bob")).toBeInTheDocument();
  });

  it("renders cell via accessorKey when no cell fn provided", () => {
    renderTable();
    expect(screen.getByText("Porto")).toBeInTheDocument();
    expect(screen.getByText("Lisbon")).toBeInTheDocument();
  });

  it("renders cell via cell() fn when provided", () => {
    const columnsWithFn: ColumnDef<Row>[] = [
      { header: "Name", cell: (row) => <span>{row.name.toUpperCase()}</span> },
    ];
    renderTable(DATA, undefined, columnsWithFn);
    expect(screen.getByText("ALICE")).toBeInTheDocument();
    expect(screen.getByText("BOB")).toBeInTheDocument();
  });

  it("shows emptyMessage when data is empty", () => {
    renderInRouter(
      <DataTable
        columns={COLUMNS}
        data={[]}
        keyExtractor={(row) => row.id}
        emptyMessage="Nothing to show."
      />,
    );
    expect(screen.getByText("Nothing to show.")).toBeInTheDocument();
  });

  it("a row is never itself a control: no button role, no tab stop", () => {
    renderTable(DATA, (r) => `/rows/${r.id}`);
    const rows = screen.getAllByRole("row").slice(1);
    rows.forEach((row) => {
      expect(row).not.toHaveAttribute("role", "button");
      expect(row).not.toHaveAttribute("tabindex");
    });
  });

  it("S03: Tab reaches the record's named link and Enter opens it", async () => {
    const user = userEvent.setup();
    renderTable(DATA, (r) => `/rows/${r.id}`, LINKED_COLUMNS(vi.fn()));
    await user.tab();
    expect(screen.getByRole("link", { name: "Alice" })).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(await screen.findByText("Opened r1")).toBeInTheDocument();
  });

  it("with rowHref, a pointer click elsewhere on the row opens the record", async () => {
    const user = userEvent.setup();
    renderTable(DATA, (r) => `/rows/${r.id}`);
    await user.click(screen.getByText("Lisbon"));
    expect(await screen.findByText("Opened r2")).toBeInTheDocument();
  });

  it("a click on the row's own button runs that button and does not open the record", async () => {
    const user = userEvent.setup();
    const onAction = vi.fn();
    renderTable(DATA, (r) => `/rows/${r.id}`, LINKED_COLUMNS(onAction));
    await user.click(screen.getByRole("button", { name: "Edit Alice" }));
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/^Opened/)).not.toBeInTheDocument();
  });

  it("a click inside a row's portalled menu or dialog does not open the record (React bubbles it to the row)", async () => {
    const user = userEvent.setup();
    const onPick = vi.fn();
    const columns: ColumnDef<Row>[] = [
      { header: "Name", cell: (row) => <RecordLink to={`/rows/${row.id}`}>{row.name}</RecordLink> },
      {
        header: "",
        cell: (row) => createPortal(<div role="menu"><span onClick={onPick}>Archive {row.name}</span></div>, document.body),
      },
    ];
    renderTable(DATA, (r) => `/rows/${r.id}`, columns);
    await user.click(screen.getByText("Archive Alice"));
    expect(onPick).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/^Opened/)).not.toBeInTheDocument();
  });

  it("applies cursor-pointer class to rows when rowHref is provided", () => {
    renderTable(DATA, (r) => `/rows/${r.id}`);
    const rows = screen.getAllByRole("row").slice(1); // skip header row
    rows.forEach((row) => expect(row).toHaveClass("cursor-pointer"));
  });

  it("does NOT apply cursor-pointer when rowHref is absent", () => {
    renderTable(DATA);
    const rows = screen.getAllByRole("row").slice(1);
    rows.forEach((row) => expect(row).not.toHaveClass("cursor-pointer"));
  });

  describe("mobileCard", () => {
    it("without mobileCard: table is wrapped in overflow-x-auto, no card list rendered", () => {
      renderTable();
      expect(screen.queryByRole("list")).not.toBeInTheDocument();
      const table = screen.getByRole("table");
      // The Table UI primitive wraps <table> in its own div — DataTable's
      // overflow-x-auto wrapper is the grandparent, not the direct parent.
      expect(table.parentElement?.parentElement).toHaveClass("overflow-x-auto");
    });

    it("with mobileCard and non-empty data: both the card list and the table wrapper are present", () => {
      renderInRouter(
        <DataTable
          columns={COLUMNS}
          data={DATA}
          keyExtractor={(row) => row.id}
          mobileCard={(row) => <span>{row.name} card</span>}
        />,
      );
      expect(screen.getByRole("list")).toBeInTheDocument();
      expect(screen.getByText("Alice card")).toBeInTheDocument();
      const table = screen.getByRole("table");
      expect(table.parentElement?.parentElement).toHaveClass("hidden", "sm:block");
    });

    it("with mobileCard and empty data: card list is not rendered, table's emptyMessage row shows without hidden sm:block", () => {
      renderInRouter(
        <DataTable
          columns={COLUMNS}
          data={[]}
          keyExtractor={(row) => row.id}
          mobileCard={(row) => <span>{row.name} card</span>}
          emptyMessage="Nothing to show."
        />,
      );
      expect(screen.queryByRole("list")).not.toBeInTheDocument();
      expect(screen.getByText("Nothing to show.")).toBeInTheDocument();
      const table = screen.getByRole("table");
      expect(table.parentElement?.parentElement).not.toHaveClass("hidden");
    });

    it("a card is a positioned container for its stretched record link, and Enter on that link opens the record", async () => {
      const user = userEvent.setup();
      renderInRouter(
        <DataTable
          columns={COLUMNS}
          data={DATA}
          keyExtractor={(row) => row.id}
          rowHref={(r) => `/rows/${r.id}`}
          mobileCard={(row) => <RecordLink to={`/rows/${row.id}`} stretched>{row.name} card</RecordLink>}
        />,
      );
      const items = screen.getAllByRole("listitem");
      items.forEach((item) => expect(item).toHaveClass("relative"));
      screen.getByRole("link", { name: "Bob card" }).focus();
      await user.keyboard("{Enter}");
      expect(await screen.findByText("Opened r2")).toBeInTheDocument();
    });

    it("rowHref also opens from a pointer click on a table row when mobileCard is present", async () => {
      const user = userEvent.setup();
      renderInRouter(
        <DataTable
          columns={COLUMNS}
          data={DATA}
          keyExtractor={(row) => row.id}
          rowHref={(r) => `/rows/${r.id}`}
          mobileCard={(row) => <span>{row.name} card</span>}
        />,
      );
      await user.click(screen.getAllByText("Alice")[0].closest("tr")!);
      expect(await screen.findByText("Opened r1")).toBeInTheDocument();
    });
  });
});
