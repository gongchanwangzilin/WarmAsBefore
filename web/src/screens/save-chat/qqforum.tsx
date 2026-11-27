import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import './savechat.css';

/**
 * QQ 论坛：Views/Chat/QqForumPage.xaml（117 行）+ QqForumViewModel。
 *
 * 顶栏（‹ · QQ 论坛）→ 话题 chips（日常 / 愿望 / 今天想做什么）→ 帖子流（作者 + 时间 + 正文，
 * 自己发的作者走信号色）→ 发帖条。帖子落盘在 storage 里，前端没有接口：
 * 没有帖子时照源码空态说「还没有帖子，来开个头吧～」（AppResources.resx 的 Forum_Empty），
 * 发帖钮保持源码样式、不做写入。
 */

type Post = { topic: string; author: string; text: string; time: string };

const TOPICS = [
  { id: 'daily', title: '日常' },
  { id: 'wish', title: '愿望' },
  { id: 'today', title: '今天想做什么' },
];

const POSTS: Post[] = [];

export function Qqforum({ back }: ScreenProps) {
  const [topic, setTopic] = useState('daily');
  const [input, setInput] = useState('');

  const posts = POSTS.filter((post) => post.topic === topic);

  return (
    <PageShell>
      <div className="sc-forum">
        <div className="sc-forum__bar">
          <button type="button" className="sc-iconbtn" aria-label="返回" onClick={back}>
            {'<'}
          </button>
          <span className="sc-forum__title">QQ 论坛</span>
          <span />
        </div>

        <div className="sc-topics">
          {TOPICS.map((item) => (
            <button
              key={item.id}
              type="button"
              className="sc-topic"
              aria-pressed={topic === item.id}
              onClick={() => setTopic(item.id)}
            >
              {item.title}
            </button>
          ))}
        </div>

        <div className="sc-posts">
          {posts.length === 0 ? (
            <p className="cap">还没有帖子，来开个头吧～</p>
          ) : (
            posts.map((post) => (
              <article key={post.text} className="sc-post">
                <div className="sc-post__head">
                  <span className={post.author === '我' ? 'sc-post__author sc-post__author--me' : 'sc-post__author'}>
                    {post.author}
                  </span>
                  <span className="sc-post__time">{post.time}</span>
                </div>
                <p className="sc-post__text">{post.text}</p>
              </article>
            ))
          )}
        </div>

        <div className="sc-forum__input">
          <textarea
            className="sc-editor"
            rows={1}
            value={input}
            placeholder="在论坛上说点什么…"
            aria-label="在论坛上说点什么…"
            onChange={(event) => setInput(event.target.value)}
          />
          <button type="button" className="sc-send">
            发帖
          </button>
        </div>
      </div>
    </PageShell>
  );
}
