/**
 * A stand-in for a Studio Settings card in the grouping tests (E05c), so they mount only the grouping and
 * not fourteen real cards and their queries. It carries the real card's data-tour attribute, where it has
 * one, and a focusable button for the keyboard tests. Each real card's own test file covers its insides
 * (PokSettingsCard.test.tsx, for one, pins its tour target).
 */
export function makeCardStub(name: string, dataTour?: string) {
  function CardStub() {
    return (
      <div data-testid={`card-${name}`} data-tour={dataTour}>
        <button type="button">{name} action</button>
      </div>
    );
  }
  CardStub.displayName = name;
  return CardStub;
}
