import { useState } from 'react';
import { GlassSwitch } from '../../components/GlassSwitch';
import { Glass } from '../../components/Glass';
import type { ScreenProps } from '../types';
import './main.css';

/**
 * 开局设置：Views/Main/SetupPage.xaml（241 行）+ SetupViewModel。
 *
 * 一屏一件事：语言 → 常驻与桌宠（只有 Windows 有这步）→ AI 端点 → 收尾。
 * 卡里左上角一枚裸返回箭头、右上角步数，右下角文字次行动 + 实心主行动，
 * 整页最右下角留给「跳过配置」。文案全部来自 AppResources 的 Setup_* 条目。
 */

type Step = 0 | 1 | 2 | 3;

const LANGS: { value: string; label: string }[] = [
  { value: 'zh-CN', label: '简体中文' },
  { value: 'zh-TW', label: '繁體中文' },
  { value: 'en-US', label: 'English' },
  { value: 'ja-JP', label: '日本語' },
];

const PETS: { value: string; label: string }[] = [
  { value: '0', label: '不主动出现' },
  { value: '5', label: '空闲 5 分钟后' },
  { value: '15', label: '空闲 15 分钟后' },
  { value: '30', label: '空闲 30 分钟后' },
];

const STEP_COUNT = 4;

export function Setup({ navigate }: ScreenProps) {
  const [step, setStep] = useState<Step>(0);
  const [lang, setLang] = useState('zh-CN');
  const [topmost, setTopmost] = useState(false);
  const [pet, setPet] = useState('0');
  const [aiUrl, setAiUrl] = useState('');
  const [aiKey, setAiKey] = useState('');
  const [aiModel, setAiModel] = useState('');

  const leave = () => navigate('/');
  const next = () => {
    if (step < 3) setStep((step + 1) as Step);
    else navigate('/select');
  };

  const option = (key: string, label: string, selected: boolean, pick: () => void) => (
    <button
      type="button"
      className="msu__opt"
      key={key}
      role="radio"
      aria-checked={selected}
      onClick={pick}
    >
      <span className="msu__opt-bar" aria-hidden="true" />
      <span className="msu__opt-label">{label}</span>
      <span />
    </button>
  );

  return (
    <div className="msu">
      <Glass as="div" tone="card" radius={12} className="msu__card">
        <div className="msu__head">
          <button
            type="button"
            className="m-btn m-btn--ghost msu__arrow"
            onClick={() => setStep((step - 1) as Step)}
            style={{ visibility: step > 0 ? 'visible' : 'hidden' }}
            aria-label="返回"
          >
            ←
          </button>
          <span />
          <span className="msu__eyebrow">{`${step + 1} / ${STEP_COUNT}`}</span>
        </div>

        <div className="msu__step">
          {step === 0 ? (
            <>
              <h2 className="msu__title">先选一种语言</h2>
              <p className="msu__sub">点了立刻换，整个软件跟着变，不用重启。</p>
              <div className="msu__opts">
                {LANGS.map((item) => option(item.value, item.label, item.value === lang, () => setLang(item.value)))}
              </div>
            </>
          ) : null}

          {step === 1 ? (
            <>
              <h2 className="msu__title">让它常在</h2>
              <p className="msu__sub">这两项只管 Windows 上的它，之后在设置里随时改回来。</p>
              <div className="msu__toggle-row">
                <span className="msu__toggle-text">
                  <b>窗口置顶</b>
                  <span>留在最前面，不被别的窗口盖住</span>
                </span>
                <GlassSwitch label="窗口置顶" checked={topmost} onChange={setTopmost} />
              </div>
              <div className="mc__sep" />
              <span className="msu__field-label">桌宠自己出现的间隔</span>
              <div className="msu__opts">
                {PETS.map((item) => option(item.value, item.label, item.value === pet, () => setPet(item.value)))}
              </div>
            </>
          ) : null}

          {step === 2 ? (
            <>
              <h2 className="msu__title">接上会说话的那部分</h2>
              <p className="msu__sub">填了才有真实回复。先跳过也行，人物和页面照常用。</p>
              <span className="msu__field-label">API 地址</span>
              <input
                className="msu__entry"
                value={aiUrl}
                placeholder="https://api.example.com/v1/chat/completions"
                aria-label="API 地址"
                onChange={(event) => setAiUrl(event.target.value)}
              />
              <span className="msu__line" />
              <span className="msu__field-label">API 密钥</span>
              <input
                className="msu__entry"
                type="password"
                value={aiKey}
                placeholder="sk-…"
                aria-label="API 密钥"
                onChange={(event) => setAiKey(event.target.value)}
              />
              <span className="msu__line" />
              <span className="msu__field-label">模型</span>
              <input
                className="msu__entry"
                value={aiModel}
                placeholder="gpt-4o"
                aria-label="模型"
                onChange={(event) => setAiModel(event.target.value)}
              />
              <span className="msu__line" />
              <p className="msu__hint">密钥只发给你填的这个地址，不进日志，也不走别处。</p>
            </>
          ) : null}

          {step === 3 ? (
            <>
              <h2 className="msu__title">就这些</h2>
              <p className="msu__sub">其余的边用边调。现在去认识第一个人？</p>
              <span className="msu__teach-head">开始之前</span>
              <div className="msu__teach">
                <span className="msu__teach-dot" aria-hidden="true" />
                <span>
                  <b>先有一位角色</b>
                  <span>新建就够：名字、性别、性格，不用立绘；也可导入角色包（.zip）</span>
                </span>
              </div>
              <div className="msu__teach">
                <span className="msu__teach-dot" aria-hidden="true" />
                <span>
                  <b>真实回复要接 AI</b>
                  <span>设置 →「AI 与模型」：填地址、密钥、模型</span>
                </span>
              </div>
              <div className="msu__teach">
                <span className="msu__teach-dot" aria-hidden="true" />
                <span>
                  <b>随时再调</b>
                  <span>外观与主题 换风格和配色；数据与素材 里导入导出素材包</span>
                </span>
              </div>
            </>
          ) : null}
        </div>

        <div className="msu__actions">
          {step > 0 ? (
            <button type="button" className="m-btn" onClick={() => setStep((step - 1) as Step)}>
              返回
            </button>
          ) : null}
          {step === 2 ? (
            <button type="button" className="m-btn" onClick={next}>
              跳过这步
            </button>
          ) : null}
          {step === 3 ? (
            <button type="button" className="m-btn" onClick={leave}>
              以后再说
            </button>
          ) : null}
          {step === 3 ? (
            <button type="button" className="m-btn m-btn--primary" onClick={() => navigate('/select')}>
              去创建角色
            </button>
          ) : (
            <button type="button" className="m-btn m-btn--primary" onClick={next}>
              下一步
            </button>
          )}
        </div>
      </Glass>

      {step === 3 ? null : (
        <button type="button" className="m-btn msu__skip" onClick={leave}>
          跳过配置
        </button>
      )}
    </div>
  );
}
