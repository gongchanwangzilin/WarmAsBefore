import type { Screen } from '../types';
import { Cloudsaves } from './cloudsaves';
import { Colorpicker } from './colorpicker';
import { MentalHealth } from './mentalHealth';
import { Petsettings } from './petsettings';
import { Petstudio } from './petstudio';
import { Usagestats } from './usagestats';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  usagestats: Usagestats,
  cloudsaves: Cloudsaves,
  colorpicker: Colorpicker,
  mentalHealth: MentalHealth,
  petsettings: Petsettings,
  petstudio: Petstudio,
};
