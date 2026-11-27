import type { ReactElement } from 'react';
import type { Bridge } from '../bridge/client';

/**
 * 每个页面的入参。页面自己不认识路由表，只拿路径、查询串和两个动作 ——
 * 所以页面可以被单独挂到任何地方（宿主推送、测试夹具），不需要知道自己在第几层。
 */
export type ScreenProps = {
  bridge: Bridge;
  /** 当前路径，含查询串，原样给后端报路 */
  path: string;
  /** 查询串（?key=appearance 这样的参数从这里取） */
  query: URLSearchParams;
  navigate: (path: string) => void;
  back: () => void;
};

export type Screen = (props: ScreenProps) => ReactElement;
