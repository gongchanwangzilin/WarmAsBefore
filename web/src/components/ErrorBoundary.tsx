import { Component, type ErrorInfo, type ReactNode } from 'react';

/**
 * 一页崩了不能把整个窗口带走。
 *
 * 之前任何一页在渲染中抛异常，React 会卸载整棵根，窗口就剩一块空底 ——
 * 用户看到的「点一下就黑了」就是这一种。现在崩溃只影响那一页，
 * 而且把错误原话留在屏幕上（同一句话也会进 #web-error，验收脚本读的就是它）。
 */
export class ErrorBoundary extends Component<
  { where: string; children: ReactNode },
  { error: Error | null }
> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    const box = document.getElementById('web-error');
    if (box) box.textContent = `${this.props.where}: ${error.message}\n${info.componentStack ?? ''}`;
    console.error('page crashed', this.props.where, error, info);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="crash" role="alert">
        <span className="crash__title">这一页没能画出来</span>
        {/* 页名 + 原话一起写：截图发出去时能直接说清是哪一页、抛的是什么。 */}
        <span className="crash__body">
          {this.props.where} · {this.state.error.message}
        </span>
        <button type="button" className="k-textbtn" onClick={() => this.setState({ error: null })}>
          重试
        </button>
      </div>
    );
  }
}
