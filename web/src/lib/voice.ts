import { useCallback, useEffect, useRef, useState } from 'react';
import type { Bridge } from '../bridge/client';
import type { ChatShell } from '../bridge/protocol';

/**
 * 语音按钮的一条通路：voice.status 问「这台设备能不能听」，voice.listen 开麦等一句话，
 * voice.cancel 停掉正在听的那句，voice.speak 交给宿主的朗读引擎。
 *
 * 识别与朗读都在宿主（SpeechService：Windows 桌面 SAPI / WinRT / 外部 API），
 * 这里只把结果填进输入框，绝不替用户按发送；拿不到麦克风或引擎时按钮停在禁用态，
 * 并把宿主给的那句原因（没麦、没授权、没装识别语言）原样摆出来。
 */

/** voice.listen 在宿主侧最多听 20 秒（SpeechService.WindowsListenSeconds），请求要等得比它久。 */
const LISTEN_TIMEOUT_MS = 30000;

export type VoiceController = {
  /** 宿主说这条路能走（开关开着、引擎/密钥在）；false 时按钮禁用并显示 statusReason。 */
  canListen: boolean;
  /** 不能听的原因（宿主原话）；空串 = 能听。 */
  statusReason: string;
  /** 上一次操作的结果：没听出字的原因、或朗读失败的原因；空串 = 无事可报。 */
  note: string;
  listening: boolean;
  /** 开一次麦，听出的文字交回调用方（空串 = 没听出来，原因在 note 里）。 */
  listen: (characterId?: string) => Promise<string>;
  /** 停掉正在听的那一句（与宿主 StopListening 同一条路）。 */
  stop: () => Promise<void>;
  /** 朗读一段文字；失败把原因写进 note。 */
  speak: (text: string, characterId?: string) => Promise<void>;
};

export function useVoice(bridge: Bridge, shell?: ChatShell): VoiceController {
  const [canListen, setCanListen] = useState(false);
  const [statusReason, setStatusReason] = useState('');
  const [note, setNote] = useState('');
  const [listening, setListening] = useState(false);
  const busy = useRef(false);

  useEffect(() => {
    let live = true;
    bridge.request('voice.status', { shell }).then(
      (status) => {
        if (!live) return;
        setCanListen(status.canListen);
        setStatusReason(status.note);
      },
      (cause) => {
        if (!live) return;
        setCanListen(false);
        setStatusReason(cause instanceof Error ? cause.message : '宿主没有回答语音状态。');
      },
    );
    return () => {
      live = false;
    };
  }, [bridge, shell]);

  const listen = useCallback(
    async (characterId?: string) => {
      if (busy.current) return '';
      busy.current = true;
      setListening(true);
      setNote('');
      try {
        const heard = await bridge.request('voice.listen', { shell, characterId }, LISTEN_TIMEOUT_MS);
        setNote(heard.note);
        return heard.text;
      } catch (cause) {
        setNote(cause instanceof Error ? cause.message : '这句话没有识别出来。');
        return '';
      } finally {
        busy.current = false;
        setListening(false);
      }
    },
    [bridge, shell],
  );

  const stop = useCallback(async () => {
    try {
      await bridge.request('voice.cancel', {});
    } catch {
      // 停不掉也只是一句还在听：listen 自己会在 20 秒上限收尾，不另报一次错。
    }
  }, [bridge]);

  const speak = useCallback(
    async (text: string, characterId?: string) => {
      setNote('');
      try {
        await bridge.request('voice.speak', { text, characterId }, LISTEN_TIMEOUT_MS);
      } catch (cause) {
        setNote(cause instanceof Error ? cause.message : '这段没有读出来。');
      }
    },
    [bridge],
  );

  return { canListen, statusReason, note, listening, listen, stop, speak };
}
