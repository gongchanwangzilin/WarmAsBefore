import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { ChatShell, type ChatContact, type ChatMessage } from './parts';
import './savechat.css';

/**
 * 微信壳：Views/Chat/WeChatPage.xaml（573 行）+ WeChatViewModel。
 *
 * 左栏竖排入口（消息/好友/收藏/表情/设置）· 会话列表（搜索 + ＋ + 每行头像/名字/最后一条/时间/未读点）
 * · 会话区（顶栏头像 + 名字 + 「本地微信 · TA 还在」+ 全屏 + 微信徽标；气泡我右她左；
 * 输入条：语音 / 礼物 / 输入 / 表情 / 文件 / 发送）。送礼面板与表情空态照抄源码。
 *
 * 消息来自 AI 会话与存档里的聊天记录，前端没有接口：会话列表照源码说「这一档还没有角色」，
 * 会话区说「还没有聊过」，顶栏名字落在 WeChatViewModel.PartnerName 的默认值「TA」。
 */

const CONTACTS: ChatContact[] = [];

const MESSAGES: ChatMessage[] = [];

export function Chat({ back }: ScreenProps) {
  return (
    <PageShell>
      <ChatShell
        skin="wechat"
        contacts={CONTACTS}
        partner="TA"
        modeLabel="本地微信 · TA 还在"
        messages={MESSAGES}
        onBack={back}
      />
    </PageShell>
  );
}
