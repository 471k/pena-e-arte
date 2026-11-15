import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "./ui/table";
import { useNavigate } from "react-router-dom";
import type { MouseEvent } from "react";
import { cn } from "@/shared/utils/cn";
import { RECORD_CONTAINER, isInsideControl } from "./recordLinkContract";

export interface ColumnDef<T> {
  header: string;
  accessorKey?: keyof T;
  cell?: (row: T) => React.ReactNode;
}

interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data: T[];
  keyExtractor: (row: T) => string;
  /**
   * Where a row's record opens (S03). The row is never the control: put a {@link RecordLink} on the record's
   * title in a column (and a `stretched` one in `mobileCard`) — that is how a keyboard or screen-reader user
   * opens it. With `rowHref` a pointer click anywhere else on the row opens it too, except on the row's own
   * buttons and links. There is deliberately no `onRowClick`: a row that only a mouse can open is the defect.
   */
  rowHref?: (row: T) => string;
  emptyMessage?: string;
  /** When provided, rows render as stacked cards below the `sm` breakpoint
   *  instead of the table. Omit to keep the existing table-only behavior
   *  (now wrapped in `overflow-x-auto` so it never regresses on narrow
   *  screens even before a page adopts the card view). */
  mobileCard?: (row: T) => React.ReactNode;
}

export function DataTable<T>({
  columns,
  data,
  keyExtractor,
  rowHref,
  emptyMessage = "No results.",
  mobileCard,
}: DataTableProps<T>) {
  const navigate = useNavigate();
  const showCards = !!mobileCard && data.length > 0;

  function openOnPointer(event: MouseEvent<HTMLElement>, row: T) {
    if (!rowHref) return;
    if (isInsideControl(event.target, event.currentTarget)) return;
    // Selecting text in a row is not a request to open it.
    if ((window.getSelection?.()?.toString() ?? "") !== "") return;
    navigate(rowHref(row));
  }

  return (
    <>
      {showCards && (
        <div className="sm:hidden flex flex-col gap-2" role="list">
          {data.map((row) => (
            <div
              key={keyExtractor(row)}
              role="listitem"
              onClick={rowHref ? (e) => openOnPointer(e, row) : undefined}
              className={cn(
                "rounded-lg border p-3",
                RECORD_CONTAINER,
                rowHref && "cursor-pointer active:bg-muted",
              )}
            >
              {mobileCard!(row)}
            </div>
          ))}
        </div>
      )}

      <div className={cn("overflow-x-auto", showCards && "hidden sm:block")}>
        <Table>
          <TableHeader>
            <TableRow>
              {columns.map((col) => (
                <TableHead key={col.header}>{col.header}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.length === 0 ? (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                  {emptyMessage}
                </TableCell>
              </TableRow>
            ) : (
              data.map((row) => (
                <TableRow
                  key={keyExtractor(row)}
                  onClick={rowHref ? (e) => openOnPointer(e, row) : undefined}
                  className={rowHref ? "cursor-pointer" : undefined}
                >
                  {columns.map((col) => (
                    <TableCell key={col.header}>
                      {col.cell
                        ? col.cell(row)
                        : col.accessorKey
                        ? String(row[col.accessorKey] ?? "")
                        : null}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>
    </>
  );
}
