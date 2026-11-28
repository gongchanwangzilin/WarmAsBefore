import { useEffect, useState } from 'react';
import type { Bridge } from '../bridge/client';

/**
 * 「现在有没有在跑一份存档」。
 *
 * 系在存档上的东西（QQ、论坛、监测、角色状态）只在存档里才有意义 ——
 * 没有存档就没有角色、没有读数，那些入口点进去只是一页空壳。
 *
 * 问不到的时候（老宿主没有 app.info、mock 桥没有这个方法）一律按「有存档」处理：
 * 宁可留一个点进去是空态的入口，也不要因为一次读失败把整排入口从界面上抹掉。
 */

export type RunState = {
  /** 已经问过宿主；在此之前页面不该按「没存档」藏东西 */
  known: boolean;
  /** 正在一份存档里 */
  active: boolean;
  /** 当前存档 id；没进存档是空串 */
  saveId: string;
};

const UNKNOWN: RunState = { known: false, active: true, saveId: '' };

export function useRun(bridge: Bridge, ticket = 0): RunState {
  const [run, setRun] = useState<RunState>(UNKNOWN);

  useEffect(() => {
    let dropped = false;
    const read = () => {
      bridge.request('app.info', {}).then(
        (info) => {
          if (!dropped) setRun({ known: true, active: info.run.active, saveId: info.run.saveId });
        },
        () => {
          if (!dropped) setRun({ known: true, active: true, saveId: '' });
        },
      );
    };
    read();
    const off = bridge.onEvent('data.changed', read);
    return () => {
      dropped = true;
      off();
    };
    // ticket 是「再问一次」：宿主这一拍没答上来时，页面可以让这个作用域重跑一遍。
  }, [bridge, ticket]);

  return run;
}

/** 灰掉入口时那一句共同的理由，按钮的 title 和空态都用它。 */
export const NEED_SAVE = '进一份存档才有 TA 的状态';
