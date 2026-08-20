import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DataTable, type ColumnDef } from "@/shared/components/DataTable";

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

function renderTable(
  data: Row[] = DATA,
  onRowClick?: (row: Row) => void,
  columns: ColumnDef<Row>[] = COLUMNS,
) {
  render(
    <DataTable
      columns={columns}
      data={data}
      keyExtractor={(row) => row.id}
      onRowClick={onRowClick}
    />,
  );
}

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
    render(
      <DataTable
        columns={COLUMNS}
        data={[]}
        keyExtractor={(row) => row.id}
        emptyMessage="Nothing to show."
      />,
    );
    expect(screen.getByText("Nothing to show.")).toBeInTheDocument();
  });

  it("calls onRowClick with the row when row is clicked", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    renderTable(DATA, onClick);
    await user.click(screen.getByText("Alice").closest("tr")!);
    expect(onClick).toHaveBeenCalledWith(DATA[0]);
  });

  it("applies cursor-pointer class to rows when onRowClick is provided", () => {
    const onClick = vi.fn();
    renderTable(DATA, onClick);
    const rows = screen.getAllByRole("row").slice(1); // skip header row
    rows.forEach((row) => expect(row).toHaveClass("cursor-pointer"));
  });

  it("does NOT apply cursor-pointer when onRowClick is absent", () => {
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
      render(
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
      render(
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

    it("onRowClick fires from a card listitem click", async () => {
      const user = userEvent.setup();
      const onClick = vi.fn();
      render(
        <DataTable
          columns={COLUMNS}
          data={DATA}
          keyExtractor={(row) => row.id}
          onRowClick={onClick}
          mobileCard={(row) => <span>{row.name} card</span>}
        />,
      );
      await user.click(screen.getByText("Alice card"));
      expect(onClick).toHaveBeenCalledWith(DATA[0]);
    });

    it("onRowClick fires from a table row click when mobileCard is also present", async () => {
      const user = userEvent.setup();
      const onClick = vi.fn();
      render(
        <DataTable
          columns={COLUMNS}
          data={DATA}
          keyExtractor={(row) => row.id}
          onRowClick={onClick}
          mobileCard={(row) => <span>{row.name} card</span>}
        />,
      );
      await user.click(screen.getAllByText("Alice")[0].closest("tr")!);
      expect(onClick).toHaveBeenCalledWith(DATA[0]);
    });
  });
});
