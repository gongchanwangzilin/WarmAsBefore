import type { Screen } from '../types';
import { Bindwizard } from './bindwizard';
import { Qv } from './qv';
import { Qqforum } from './qqforum';
import { Save } from './save';
import { SaveTree } from './saveTree';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  save: Save,
  saveTree: SaveTree,
  qv: Qv,
  qqforum: Qqforum,
  bindwizard: Bindwizard,
};
