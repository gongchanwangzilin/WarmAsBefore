import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { ChatShell, type ChatContact, type ChatMessage } from './parts';
import './savechat.css';

/**
 * QQ 壳：Views/Chat/QqPage.xaml（335 行），复用 WeChatViewModel（构造后 SetShell("qq")）。
 *
 * 与微信同构：联系人层（返回 · QQ · 视频号 / 每行 48 头像 + 名字 + 最后一条 + 未读块）、
 * 会话顶栏（返回 · 头像 · 名字 · 模式 · QQ 徽标 · 论坛）、同一套气泡、表情面板与输入条。
 * 差别就是源码写下的那三条：没有礼物/支付面板、多一个「论坛」入口、入口里有「视频号」。
 *
 * 好友来自存档里的聊天记录，前端没有接口：列表照源码说「还没有好友」，
 * 顶栏名字落在 WeChatViewModel.PartnerName 的默认值「TA」。
 */

const CONTACTS: ChatContact[] = [];

const MESSAGES: ChatMessage[] = [];

export function Qqchat({ navigate, back }: ScreenProps) {
  return (
    <PageShell>
      <ChatShell
        skin="qq"
        contacts={CONTACTS}
        partner="TA"
        modeLabel="本地 QQ · TA 还在"
        messages={MESSAGES}
        onBack={back}
        onForum={() => navigate('/qqforum')}
      />
    </PageShell>
  );
}
