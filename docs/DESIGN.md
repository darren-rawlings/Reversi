# Reversi: design

Store title: **Reversi** (the generic name; "Othello" is a trademark). Package `com.renegadegames.reversi`.

## The game
Classic 8×8 Reversi. Dark moves first. Place a disc so that it outflanks a straight line of the
opponent's discs (in any of eight directions); every outflanked disc turns to your colour. If you
can't move, you pass; when neither side can move, the most discs wins.

## Feel
- Two-coloured discs on a board in the chosen material (Style menu: the studio's five board
  materials; disc sets Red & Yellow, Walnut & Maple, Copper & Silver, Ruby & Sapphire, Onyx & Ivory).
  Default: Onyx & Ivory on oak, the classic black and white.
- A new disc drops onto its square with a clack (disc and board materials both heard).
- **Signature effect, the flip wave:** outflanked discs turn over in the air one ring at a time,
  rippling out from the new disc, each with a click and a warm glint as it lands.
- Glowing dots mark the legal squares. Drag sideways to spin the board.

## Modes
Player vs player on one phone; vs computer Easy (1-ply, a third random), Medium (3-ply), Hard
(6-ply alpha-beta on bitboards with corner/mobility evaluation, exact search of the last 10 squares).

## Ads
Between-games interstitial every 5th game; Undo (ad) vs the computer takes back your last move.

## Later
Move history and replay; a timer mode; board sizes 6×6 and 10×10.
