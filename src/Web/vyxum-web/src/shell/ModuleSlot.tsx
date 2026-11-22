import { Component, type ErrorInfo, type ReactNode } from 'react';
import type { ModuleSlotName } from './moduleLoader';
import { useModules } from './ModulesProvider';
import { useModuleUi } from './ModuleUiProvider';

/** Keeps an error in a module's slot component inside it: the screen around it keeps working and shows nothing there. */
class SlotErrorBoundary extends Component<{ moduleId: string; slot: string; children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error(`The '${this.props.slot}' component of module '${this.props.moduleId}' failed.`, error, info.componentStack);
  }

  override render() {
    return this.state.failed ? null : this.props.children;
  }
}

/**
 * A named place of a screen that modules may fill (SDK 3). Renders the components the loaded, not locked modules
 * registered for `name`, each with `props`, in module order; renders nothing when no module fills it.
 */
export function ModuleSlot<P extends object>({ name, props, className }: { name: ModuleSlotName; props: P; className?: string }) {
  const { slotItems } = useModuleUi();
  const { hasCapability } = useModules();
  const items = slotItems.filter((item) => item.slot === name && (item.capability === undefined || hasCapability(item.capability)));
  if (items.length === 0) return null;
  return (
    <div className={className} data-slot={name}>
      {items.map(({ moduleId, component: Item }) => (
        <div key={moduleId} data-module={moduleId}>
          <SlotErrorBoundary moduleId={moduleId} slot={name}>
            <Item {...props} />
          </SlotErrorBoundary>
        </div>
      ))}
    </div>
  );
}
