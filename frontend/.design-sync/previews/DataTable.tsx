import { DataTable } from 'pena-e-arte-ui';

type Client = { id: string; name: string; email: string; role: string };

const columns = [
  { header: 'Name', accessorKey: 'name' as keyof Client },
  { header: 'Email', accessorKey: 'email' as keyof Client },
  { header: 'Role', accessorKey: 'role' as keyof Client },
];

const data: Client[] = [
  { id: '1', name: 'Alice Martin', email: 'alice@studio.pt', role: 'Artist' },
  { id: '2', name: 'Bruno Costa', email: 'bruno@studio.pt', role: 'Client' },
  { id: '3', name: 'Camille Roy', email: 'camille@studio.pt', role: 'Owner' },
];

export function Default() {
  return <DataTable columns={columns} data={data} keyExtractor={(row) => row.id} />;
}

export function Empty() {
  return (
    <DataTable
      columns={columns}
      data={[]}
      keyExtractor={(row) => row.id}
      emptyMessage="No clients yet."
    />
  );
}
