import type { ReactNode } from 'react';

export interface PageHeaderProps {
  /** Already translated page title. */
  title: string;
  /** Small mono line above the title (for example "Records / 21.11.2026"). */
  eyebrow?: ReactNode;
  /** Buttons on the right, aligned to the bottom of the title. */
  actions?: ReactNode;
}

/** Title row shared by all screens, as in the design sources. */
export function PageHeader({ title, eyebrow, actions }: PageHeaderProps) {
  return (
    <header className="page-header">
      <div className="page-header__text">
        {eyebrow !== undefined ? <span className="page-header__eyebrow">{eyebrow}</span> : null}
        <h1 className="page-header__title">{title}</h1>
      </div>
      {actions !== undefined ? <div className="page-header__actions">{actions}</div> : null}
    </header>
  );
}
