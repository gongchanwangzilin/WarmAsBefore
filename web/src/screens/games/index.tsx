import type { Screen } from '../types';
import { AffectionLevelUp } from './affection-level-up';
import { CgView } from './cg-view';
import { Flows } from './flows';
import { Game } from './game';
import { Map } from './map';
import { MapCapture } from './mapcapture';
import { MapCreate } from './mapcreate';
import { Minimal3D } from './minimal3d';
import { Pet } from './pet';
import { Shop } from './shop';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  game: Game,
  map: Map,
  mapcreate: MapCreate,
  mapcapture: MapCapture,
  minimal3d: Minimal3D,
  flows: Flows,
  shop: Shop,
  'affection-level-up': AffectionLevelUp,
  'cg-view': CgView,
  pet: Pet,
};
