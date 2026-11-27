import type { Screen } from '../types';
import { Main } from './main';
import { Phone } from './phone';
import { Select } from './select';
import { Setup } from './setup';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  main: Main,
  phone: Phone,
  select: Select,
  setup: Setup,
};
