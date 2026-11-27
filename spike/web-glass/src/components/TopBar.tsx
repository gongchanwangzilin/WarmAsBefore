import { Glass } from './Glass';

type Props = {
  title: string;
  onBack: () => void;
};

export function TopBar({ title, onBack }: Props) {
  return (
    <Glass as="header" tone="bar" radius={0} className="topbar">
      <button type="button" className="topbar__back" onClick={onBack}>
        返回
      </button>
      <h1 className="topbar__title">{title}</h1>
    </Glass>
  );
}
