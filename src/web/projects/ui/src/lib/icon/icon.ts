import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Thin outline icons in the spirit of the CSoon template's Ionicons, inlined so there is no icon font to load. */
const GLYPHS = {
  brush: 'M30 6l12 12-16 16-12-12zM14 22l-6 6c-3 3-2 8-6 12 6 0 11 0 14-3l6-6',
  people:
    'M17 22a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM4 40c0-8 6-13 13-13s13 5 13 13M33 22a6 6 0 1 0 0-12M36 27c5 1 8 6 8 13',
  heart: 'M24 41S6 30 6 17a9 9 0 0 1 18-2 9 9 0 0 1 18 2c0 13-18 24-18 24z',
  spark: 'M24 4v10M24 34v10M4 24h10M34 24h10M10 10l7 7M31 31l7 7M38 10l-7 7M17 31l-7 7',
  chat: 'M6 10h36v22H20l-9 8v-8H6z',
  briefcase: 'M6 14h36v26H6zM17 14V8h14v6M6 24h36',
  leaf: 'M10 38C10 18 22 8 40 8c0 18-10 30-30 30zM10 38l16-16',
  pin: 'M24 44s14-14 14-25a14 14 0 0 0-28 0c0 11 14 25 14 25zM24 24a5 5 0 1 0 0-10 5 5 0 0 0 0 10z',
  phone: 'M12 6h8l3 9-5 3a24 24 0 0 0 12 12l3-5 9 3v8c0 2-2 4-4 4C20 40 8 28 8 10c0-2 2-4 4-4z',
  mail: 'M6 12h36v24H6zM6 12l18 14 18-14',
  clock: 'M24 44a20 20 0 1 0 0-40 20 20 0 0 0 0 40zM24 12v12l8 6',
  instagram:
    'M14 6h20a8 8 0 0 1 8 8v20a8 8 0 0 1-8 8H14a8 8 0 0 1-8-8V14a8 8 0 0 1 8-8zM24 32a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM35 13h0',
  cart: 'M4 8h6l5 22h24l4-16H13M18 40a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM35 40a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  menu: 'M6 13h36M6 24h36M6 35h36',
  grid: 'M8 8h13v13H8zM27 8h13v13H27zM8 27h13v13H8zM27 27h13v13H27z',
  calendar: 'M6 10h36v32H6zM6 19h36M15 5v9M33 5v9',
  close: 'M10 10l28 28M38 10L10 38',
  arrow: 'M6 24h34M28 12l12 12-12 12',
  frame: 'M6 8h36v32H6zM12 14h24v20H12zM12 30l8-8 6 6 4-4 6 6',
  ticket: 'M4 14h40v6a4 4 0 0 0 0 8v6H4v-6a4 4 0 0 0 0-8zM30 14v20',
  star: 'M24 5l6 13 14 1-11 9 4 14-13-8-13 8 4-14L4 19l14-1z',
  gift: 'M6 18h36v8H6zM9 26h30v16H9zM24 18v24M24 18c-4-8-12-8-12-3s8 3 12 3c4 0 12 2 12-3s-8-5-12 3',
  infinity:
    'M24 24c-5-6-9-9-13-9a9 9 0 0 0 0 18c4 0 8-3 13-9zm0 0c5 6 9 9 13 9a9 9 0 0 0 0-18c-4 0-8 3-13 9z',
  // status glyphs for notices, field errors and status chips
  check: 'M9 25l10 10 20-21',
  alert: 'M24 6l20 36H4zM24 19v11M24 36h0',
  info: 'M24 44a20 20 0 1 0 0-40 20 20 0 0 0 0 40zM24 22v12M24 15h0',
  plus: 'M24 8v32M8 24h32',
  stop: 'M16 4h16l12 12v16L32 44H16L4 32V16zM18 18l12 12M30 18L18 30',
} as const satisfies Record<string, string>;

export type IconName = keyof typeof GLYPHS;

export const ICON_NAMES = Object.keys(GLYPHS) as readonly IconName[];

/**
 * A thin line icon. Decorative (aria-hidden) unless `label` is given, in which case it is announced as an image.
 * The host keeps the `icon` class so app styles such as `.feature .icon` keep matching.
 */
@Component({
  selector: 'ui-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'icon',
    '[style.--icon-size]': 'size() + "px"',
    '[attr.aria-hidden]': 'label() ? null : "true"',
    '[attr.role]': 'label() ? "img" : null',
    '[attr.aria-label]': 'label() || null',
  },
  templateUrl: './icon.html',
  styleUrl: './icon.scss',
})
export class UiIcon {
  readonly name = input.required<IconName>();
  readonly size = input(24);
  readonly stroke = input(1.6);
  /** Accessible name. Leave empty for decorative icons next to visible text. */
  readonly label = input<string>();
  protected readonly d = computed(() => GLYPHS[this.name()]);
}
