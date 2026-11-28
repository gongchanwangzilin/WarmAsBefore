import { useState } from 'react';
import { Segmented, type SegmentedOption } from '../../components/Segmented';
import { Icon, type IconName } from './qqicons';

/**
 * QQ 设置面板：真机客户端「设置」那扇窗（第三张参照截图）。
 *
 * 左列分组、右列内容的分组页；「通用」这一组是参照截图里看得见的那一屏：
 * 界面模式两张缩略卡、外观设置三张缩略卡、聊天四条设置行。
 * 分组名与每一行的标题、副说明、下拉框里的现值都照截图原文抄下来；
 * 只有「通用」里的选择是本地状态 —— 落盘要 qq.settings 桥方法（报告里列了）。
 */

type Section = { id: string; label: string; icon: IconName; ready: boolean };

const SECTIONS: Section[] = [
  { id: 'general', label: '通用', icon: 'gear', ready: true },
  { id: 'notice', label: '消息通知', icon: 'bell', ready: false },
  { id: 'storage', label: '存储管理', icon: 'storage', ready: false },
  { id: 'call', label: '音视频通话', icon: 'video', ready: false },
  { id: 'keys', label: '快捷键', icon: 'keyboard', ready: false },
  { id: 'permission', label: '权限设置', icon: 'shield', ready: false },
  { id: 'login', label: '登录设置', icon: 'key', ready: false },
  { id: 'binding', label: '账号绑定与授权', icon: 'link', ready: false },
  { id: 'palette', label: '超级调色盘', icon: 'palette', ready: false },
  { id: 'about', label: '关于QV', icon: 'info', ready: false },
];

/** 界面模式：真机里「效率模式」是左列表右会话的三栏，「经典模式」只有会话。 */
type UiMode = 'efficiency' | 'classic';
/** 外观：白天 / 夜间 / 跟随系统。 */
type Look = 'day' | 'night' | 'system';

const LOOKS: SegmentedOption<Look>[] = [
  { value: 'day', label: '白天' },
  { value: 'night', label: '夜间' },
  { value: 'system', label: '跟随系统' },
];

const SELECT_CLOSED = 'settings 桥还没有接上，下拉框停在原地';

export function QqSettings({ onClose, onTheme }: { onClose: () => void; onTheme: () => void }) {
  const [section, setSection] = useState('general');
  const [mode, setMode] = useState<UiMode>('efficiency');
  const [look, setLook] = useState<Look>('system');

  return (
    <div className="qw-set" role="dialog" aria-modal="true" aria-label="设置">
      <div className="qw-set__win">
        <div className="qw-set__bar">
          <span className="qw-set__brand">
            <Icon name="gear" />
            设置
          </span>
          <button type="button" className="qw-set__close" aria-label="关闭设置" onClick={onClose}>
            <Icon name="close" />
          </button>
        </div>

        <div className="qw-set__body">
          <nav className="qw-set__nav" aria-label="设置分组">
            {SECTIONS.map((item) => (
              <button
                key={item.id}
                type="button"
                className="qw-set__navitem"
                aria-current={section === item.id}
                disabled={!item.ready}
                title={item.ready ? undefined : '这一组的设置项还没有接到 Web 端'}
                onClick={() => setSection(item.id)}
              >
                <Icon name={item.icon} />
                <span>{item.label}</span>
              </button>
            ))}
          </nav>

          <div className="qw-set__pane">
            <h2 className="qw-set__title">通用</h2>

            <h3 className="qw-set__sect">界面模式</h3>
            <div className="qw-set__cards">
              {(
                [
                  { id: 'efficiency', label: '效率模式', layout: 'three' },
                  { id: 'classic', label: '经典模式', layout: 'one' },
                ] as const
              ).map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className="qw-set__card"
                  aria-pressed={mode === item.id}
                  onClick={() => setMode(item.id)}
                >
                  <span className="qw-thumb" data-layout={item.layout} aria-hidden="true">
                    <i className="qw-thumb__rail" />
                    <i className="qw-thumb__list">
                      <b />
                      <b />
                      <b />
                    </i>
                    <i className="qw-thumb__chat">
                      <b />
                      <b />
                    </i>
                  </span>
                  <span className="qw-set__cardlabel">{item.label}</span>
                </button>
              ))}
            </div>

            <h3 className="qw-set__sect">外观设置</h3>
            <Segmented label="外观" size="md" value={look} options={LOOKS} onChange={setLook} />

            <button type="button" className="qw-set__link" onClick={onTheme}>
              切换色彩主题
            </button>

            <h3 className="qw-set__sect">聊天</h3>
            <div className="qw-set__rows">
              <div className="qw-set__row">
                <span className="qw-set__rowtext">
                  <b>发送消息</b>
                </span>
                <span className="qw-set__select" title={SELECT_CLOSED}>
                  Enter
                </span>
              </div>

              <div className="qw-set__row">
                <span className="qw-set__rowtext">
                  <b>双击会话打开独立窗口</b>
                  <small>仅效率模式生效</small>
                </span>
                <button
                  type="button"
                  className="qw-switch"
                  role="switch"
                  aria-checked={false}
                  aria-label="双击会话打开独立窗口"
                  disabled
                  title={SELECT_CLOSED}
                >
                  <i />
                </button>
              </div>

              <div className="qw-set__row">
                <span className="qw-set__rowtext">
                  <b>打开新聊天窗口的方式</b>
                </span>
                <span className="qw-set__select" title={SELECT_CLOSED}>
                  自动合并窗口
                </span>
              </div>

              <div className="qw-set__row">
                <span className="qw-set__rowtext">
                  <b>根据输入内容推荐表情</b>
                </span>
                <button
                  type="button"
                  className="qw-switch"
                  role="switch"
                  aria-checked

                  aria-label="根据输入内容推荐表情"
                  disabled
                  title={SELECT_CLOSED}
                >
                  <i />
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
