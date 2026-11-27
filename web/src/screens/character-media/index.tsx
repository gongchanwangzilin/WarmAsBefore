import type { Screen } from '../types';
import { Gallery } from './gallery';
import { Outfit } from './outfit';
import { FigurePack } from './figurePack';
import { Materials } from './materials';
import { PortraitCalibration } from './portraitCalibration';
import { Avatarcrop } from './avatarcrop';
import { MemoryManage } from './memoryManage';
import { MemoryGraph } from './memoryGraph';
import { MemorySearch } from './memorySearch';
import { SuperMonitor } from './superMonitor';
import { ModuleInspector } from './moduleInspector';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  gallery: Gallery,
  outfit: Outfit,
  figurePack: FigurePack,
  materials: Materials,
  portraitCalibration: PortraitCalibration,
  avatarcrop: Avatarcrop,
  memoryManage: MemoryManage,
  memoryGraph: MemoryGraph,
  memorySearch: MemorySearch,
  superMonitor: SuperMonitor,
  moduleInspector: ModuleInspector,
};
