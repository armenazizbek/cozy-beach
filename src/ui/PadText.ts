/** Whether the player is on a controller right now; hints and labels then name its buttons instead of keys. */
export const pad = { on: false };

const SWAPS: [RegExp, string][] = [
  [/W \/ S throttle and reverse · A \/ D steer · Space slows · F leaves the wheel \(to use a weapon\) · E steps off when parked at the beach/, 'RT / LT throttle and reverse · left stick steers · B slows · A leaves the wheel (to use a weapon) · X steps off when parked at the beach'],
  [/W \/ S drive and reverse · A \/ D steer · Space brakes · F gets out/, 'RT / LT drive and reverse · left stick steers · B brakes · A gets out'],
  [/Move the mouse toward a screen edge to turn · click to use your tool/, 'Right stick looks around · RT uses your tool'],
  [/Press 7 or scroll to take them out; F beside a flag/, 'Use LB / RB or d-pad left to take them out; A beside a flag'],
  [/Press 7 to take them out/, 'Press d-pad left to take them out'],
  [/[Ss]croll to pick it up/, 'Use LB / RB to pick it up'],
  [/[Pp]ick it with the scroll wheel/g, 'Pick it with LB / RB'],
  [/Click to fire the harpoon · F picks up a find · Q swims back up · WASD swim · Space up · C down/, 'RT fires the harpoon · A picks up a find · X swims back up · left stick swims · RB up · LB down'],
  [/Climb the stairs and the ladder, then press F at the top to go back out/, 'Climb the stairs and the ladder, then press A at the top to go back out'],
  [/Press F/g, 'Press A'],
  [/any key/g, 'any button'],
  [/right-click/gi, 'LT'],
  [/Hold click/g, 'Hold RT'],
  [/Click it/g, 'Press RT on it'],
  [/Click: /g, 'RT: '],
  [/Click to /g, 'Press RT to '],
  [/click to /g, 'press RT to '],
  [/\bF (beside|leaves|gets)/g, 'A $1'],
  [/\bE steps/g, 'X steps'],
  [/\(Esc\)/g, '(B)'],
  [/\(Space\)/g, '(A)'],
];

/** Rewrites a keyboard-and-mouse sentence for a controller. Unchanged while the keyboard is in use. */
export const padText = (t: string): string => (pad.on ? SWAPS.reduce((s, [re, to]) => s.replace(re, to), t) : t);
