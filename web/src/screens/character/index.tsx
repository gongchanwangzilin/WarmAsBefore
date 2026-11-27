import type { Screen } from '../types';
import { NewCharacter } from './newcharacter';
import { Roster } from './roster';
import { Studio } from './studio';
import { SubProfile } from './subProfile';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  roster: Roster,
  studio: Studio,
  newcharacter: NewCharacter,
  subProfile: SubProfile,
};
