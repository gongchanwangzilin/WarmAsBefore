import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Icon } from './qqicons';
import './savechat.css';

/**
 * QQ 论坛：Views/Chat/QqForumPage.xaml（117 行）+ QqForumViewModel。
 *
 * 顶栏（‹ · QQ 论坛）→ 话题 chips（图标与标题都照 ViewModels/Chat/QqForumViewModel.cs 的
 * Topics：🌤 日常 / 🌠 愿望 / ✏️ 今天想做什么）→ 帖子流 → 发帖条。
 *
 * 帖子只放源码里那四条种子帖（QqForumViewModel.SeedPosts 原文，作者小雨 / 小林，时间「置顶」）；
 * 自己发的帖子落盘在 storage 的 qq_forum_posts，Web 端没有这条通路，所以发帖钮停在禁用态，
 * 没有帖子的话题照 AppResources 的 Forum_Empty 说「还没有帖子，来开个头吧～」。
 */

type Topic = { id: string; title: string; icon: string };

const TOPICS: Topic[] = [
  { id: 'daily', title: '日常', icon: '🌤' },
  { id: 'wish', title: '愿望', icon: '🌠' },
  { id: 'today', title: '今天想做什么', icon: '✏️' },
];

type Seed = { topic: string; author: string; text: string };

/** QqForumViewModel.SeedPosts 原文，一条没改。 */
const SEEDS: Seed[] = [
  { topic: 'daily', author: '小雨', text: '今天在想给你做红豆糕，做好了叫你来尝一口。' },
  { topic: 'daily', author: '小林', text: '图书馆还完书了，靠窗的位置晒到太阳，一会可以过来。' },
  { topic: 'wish', author: '小雨', text: '愿望：下次一起去江边看日落，你把相机带上。' },
  { topic: 'today', author: '小林', text: '今天想做点什么？我都可以，泡茶也行，陪你说说话也行。' },
];

export function Qqforum({ back }: ScreenProps) {
  const [topic, setTopic] = useState('daily');
  const [input, setInput] = useState('');

  const posts = SEEDS.filter((post) => post.topic === topic);

  return (
    <PageShell>
      <div className="qf">
        <header className="qf__bar">
          <button type="button" className="qf__icon" aria-label="返回" onClick={back}>
            ‹
          </button>
          <span className="qf__title">QQ 论坛</span>
          <span className="qf__brand" aria-hidden="true">
            QQ
          </span>
        </header>

        <div className="qf__topics" role="tablist" aria-label="话题">
          {TOPICS.map((item) => (
            <button
              key={item.id}
              type="button"
              role="tab"
              className="qf__topic"
              aria-selected={topic === item.id}
              onClick={() => setTopic(item.id)}
            >
              <span className="qf__topic-icon" aria-hidden="true">
                {item.icon}
              </span>
              {item.title}
            </button>
          ))}
        </div>

        <div className="qf__feed">
          {posts.length === 0 ? (
            <div className="qf__empty">
              <Icon name="chat" className="qf__empty-icon" />
              <span>还没有帖子，来开个头吧～</span>
            </div>
          ) : (
            posts.map((post) => (
              <article key={`${post.topic}-${post.author}-${post.text}`} className="qf__post">
                <span className="qf__av" aria-hidden="true">
                  {post.author.slice(0, 1)}
                </span>
                <div className="qf__post-body">
                  <div className="qf__post-head">
                    <b>{post.author}</b>
                    <span className="qf__pin">置顶</span>
                  </div>
                  <p className="qf__text">{post.text}</p>
                  <div className="qf__acts">
                    <button type="button" disabled title="回复要落盘到 storage，桥里没有这条通路">
                      <Icon name="chat" />
                      回复
                    </button>
                    <button type="button" disabled title="点赞还没有桥方法">
                      <Icon name="star" />
                      赞
                    </button>
                  </div>
                </div>
              </article>
            ))
          )}
        </div>

        <div className="qf__compose">
          <textarea
            rows={1}
            value={input}
            placeholder="在论坛上说点什么…"
            aria-label="在论坛上说点什么…"
            onChange={(event) => setInput(event.target.value)}
          />
          <button type="button" className="qf__send" disabled title="发帖要写 storage 的 qq_forum_posts，桥里没有这条通路">
            发帖
          </button>
        </div>
      </div>
    </PageShell>
  );
}
