import { Avatar, AvatarFallback } from 'tattoos-ui';

export function WithFallback() {
  return (
    <Avatar>
      <AvatarFallback>JD</AvatarFallback>
    </Avatar>
  );
}

export function ArtistAvatar() {
  return (
    <Avatar>
      <AvatarFallback>AM</AvatarFallback>
    </Avatar>
  );
}

export function Sizes() {
  return (
    <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
      <Avatar style={{ width: '32px', height: '32px' }}>
        <AvatarFallback style={{ fontSize: '11px' }}>SM</AvatarFallback>
      </Avatar>
      <Avatar>
        <AvatarFallback>MD</AvatarFallback>
      </Avatar>
      <Avatar style={{ width: '56px', height: '56px' }}>
        <AvatarFallback style={{ fontSize: '20px' }}>LG</AvatarFallback>
      </Avatar>
    </div>
  );
}
