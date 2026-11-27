import type { Screen } from '../types';
import { Chatimport } from './chatimport';
import { Forcecast } from './forcecast';
import { Newrun } from './newrun';
import { NovelSelect } from './novelselect';
import { Novelworld } from './novelworld';
import { SceneCalibrationPage } from './SceneCalibrationPage';
import { Scenelibrary } from './scenelibrary';
import { ShowcaseEdit } from './showcase-edit';
import { ShowcaseList } from './showcase-list';
import { ShowcasePlay } from './showcase-play';
import { Worldbook } from './worldbook';

/** 剧情组：键就是路由名。 */
export const screens: Record<string, Screen> = {
  newrun: Newrun,
  worldbook: Worldbook,
  chatimport: Chatimport,
  scenelibrary: Scenelibrary,
  SceneCalibrationPage,
  forcecast: Forcecast,
  'showcase-list': ShowcaseList,
  'showcase-edit': ShowcaseEdit,
  'showcase-play': ShowcasePlay,
  novelselect: NovelSelect,
  novelworld: Novelworld,
};
