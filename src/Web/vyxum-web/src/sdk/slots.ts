// Props of the slots the application offers to module screens (SDK 3, see MODULE_SLOTS in contract.ts).
// Only add fields; never remove or change one, so components built for an earlier version keep working.

/** Props of the `weighing.panel` slot: what the weighing screen shows and did last. */
export interface WeighingPanelProps {
  /** The selected scale, or null when none is chosen. */
  deviceId: string | null;
  /** The selected product, or null. */
  productId: string | null;
  /** The lot typed on the screen, or null. */
  lot: string | null;
  /** The scale's latest reading, or null before the first one. `value` is an invariant decimal string. */
  weight: { value: string | null; unit: string | null; state: string } | null;
  /** A save is in progress. */
  saving: boolean;
  /** The last record saved on this screen for the selected scale, or null. */
  lastRecord: {
    id: number;
    number: string;
    deviceId: string;
    productId: string | null;
    /** Saved with "Save and print label". */
    printLabel: boolean;
  } | null;
}
