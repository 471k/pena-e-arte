import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "./ui/table";
import { cn } from "@/shared/utils/cn";

export interface ColumnDef<T> {
  header: string;
  accessorKey?: keyof T;
  cell?: (row: T) => React.ReactNode;
}

interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data: T[];
  keyExtractor: (row: T) => string;
  onRowClick?: (row: T) => void;
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
  onRowClick,
  emptyMessage = "No results.",
  mobileCard,
}: DataTableProps<T>) {
  const showCards = !!mobileCard && data.length > 0;

  return (
    <>
      {showCards && (
        <div className="sm:hidden flex flex-col gap-2" role="list">
          {data.map((row) => (
            <div
              key={keyExtractor(row)}
              role="listitem"
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={cn(
                "rounded-lg border p-3",
                onRowClick && "cursor-pointer active:bg-muted",
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
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  className={onRowClick ? "cursor-pointer" : undefined}
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
