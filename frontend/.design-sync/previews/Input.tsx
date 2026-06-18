import { Input } from 'pena-e-arte-ui';

export function Default() {
  return (
    <div style={{ padding: '8px' }}>
      <Input placeholder="Client name..." />
    </div>
  );
}

export function WithValue() {
  return (
    <div style={{ padding: '8px' }}>
      <Input defaultValue="alice@studio.pt" type="email" />
    </div>
  );
}

export function Disabled() {
  return (
    <div style={{ padding: '8px' }}>
      <Input placeholder="Read-only field" disabled />
    </div>
  );
}
