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

const LANGS: { value: string; label: string; available?: boolean }[] = [
  { value: 'zh-CN', label: '简体中文', available: true },
  { value: 'en-US', label: 'English', available: true },
  // 宿主 settings.patch 的 language.locale 只收这两种；另外两种选项还写不进去，先明说。
  { value: 'zh-TW', label: '繁體中文' },
  { value: 'ja-JP', label: '日本語' },
];

const PETS: { value: string; label: string }[] = [
  { value: '0', label: '不主动出现' },
  { value: '5', label: '空闲 5 分钟后' },
  { value: '15', label: '空闲 15 分钟后' },
  { value: '30', label: '空闲 30 分钟后' },
];

const STEP_COUNT = 4;

export function Setup({ bridge, navigate }: ScreenProps) {
  const [step, setStep] = useState<Step>(0);
  const [lang, setLang] = useState('zh-CN');
  const [topmost, setTopmost] = useState(false);
  const [pet, setPet] = useState('0');
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);

  /**
   * 离开向导：把这一页真能写的那两项交给 settings.patch（语言、桌宠自现间隔），
   * 写成功才走；写失败留在原地把那句话上屏，别让「下一步」变成一个假动作。
   * 窗口置顶与 AI 端点在这条桥上没有写入路径，页面上已经分别说明。
   */
  const persist = async (next: string): Promise<void> => {
    if (busy) return;
    setBusy(true);
    setStatus('');
    try {
      await bridge.request('settings.patch', {
        patch: {
          'language.locale': lang,
          'platform.petIdleMinutes': Number(pet),
        },
      });
      navigate(next);
    } catch (cause) {
      setStatus(cause instanceof Error ? cause.message : '设置没有写进盘，先留在这一页。');
    } finally {
      setBusy(false);
    }
  };

  const next = () => {
    if (step < 3) setStep((step + 1) as Step);
    else void persist('/select');
  };

  const option = (key: string, label: string, selected: boolean, pick: () => void, unavailable?: boolean) => (
    <button
      type="button"
      className="msu__opt"
      key={key}
      role="radio"
      aria-checked={selected}
      aria-disabled={unavailable || undefined}
      disabled={unavailable}
      title={unavailable ? '这条路还写不进去：宿主的语言设置目前只认简体中文与 English' : undefined}
      onClick={pick}
    >
      <span className="msu__opt-bar" aria-hidden="true" />
      <span className="msu__opt-label">{label}</span>
      <span className="cap">{unavailable ? '暂不支持' : ''}</span>
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
              <p className="msu__sub">选好之后存进设置；宿主重新载入这一页时生效。</p>
              <div className="msu__opts">
                {LANGS.map((item) =>
                  option(
                    item.value,
                    item.label,
                    item.value === lang,
                    () => setLang(item.value),
                    !item.available,
                  ),
                )}
              </div>
            </>
          ) : null}

          {step === 1 ? (
            <>
              <h2 className="msu__title">让它常在</h2>
              <p className="msu__sub">桌宠这一项会跟着你选的就存下来；窗口置顶要等宿主给出写法，先摆着不能改。</p>
              <div className="msu__toggle-row">
                <span className="msu__toggle-text">
                  <b>窗口置顶</b>
                  <span>留在最前面，不被别的窗口盖住</span>
                </span>
                <GlassSwitch label="窗口置顶" checked={topmost} onChange={setTopmost} disabled />
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
              <p className="msu__sub">AI 地址、密钥与模型只能从设置页写进盘，这一页填了也存不下，所以不在这里收。</p>
              <div className="msu__teach">
                <span className="msu__teach-dot" aria-hidden="true" />
                <span>
                  <b>设置 →「AI 与模型」→「AI 厂商」</b>
                  <span>填端点（OpenAI 兼容）、密钥与首款模型；密钥只留在本机，不进日志。</span>
                </span>
              </div>
              <button
                type="button"
                className="m-btn m-btn--primary msu__jump"
                onClick={() => void persist('/settingssection?key=ai')}
                disabled={busy}
              >
                现在去填
              </button>
              <p className="msu__hint">先跳过也行：人物、立绘与页面照常用，只是说话会走本地的兜底回复。</p>
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
            <button type="button" className="m-btn" onClick={() => void persist('/')} disabled={busy}>
              以后再说
            </button>
          ) : null}
          {step === 3 ? (
            <button type="button" className="m-btn m-btn--primary" onClick={() => void persist('/select')} disabled={busy}>
              {busy ? '正在保存…' : '去创建角色'}
            </button>
          ) : (
            <button type="button" className="m-btn m-btn--primary" onClick={next} disabled={busy}>
              下一步
            </button>
          )}
        </div>
        {status ? <p className="msu__hint msu__hint--warn">{status}</p> : null}
      </Glass>

      {step === 3 ? null : (
        <button type="button" className="m-btn msu__skip" onClick={() => void persist('/')} disabled={busy}>
          跳过配置
        </button>
      )}
    </div>
  );
}
