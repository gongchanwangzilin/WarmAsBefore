import type { Screen } from '../types';
import { Bindwizard } from './bindwizard';
import { Chat } from './chat';
import { Qqchat } from './qqchat';
import { Qqforum } from './qqforum';
import { Save } from './save';
import { SaveTree } from './saveTree';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  save: Save,
  saveTree: SaveTree,
  chat: Chat,
  qqchat: Qqchat,
  qqforum: Qqforum,
  bindwizard: Bindwizard,
};
