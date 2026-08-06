import type { Pos } from "../api/types";
import { ATTR_CATEGORY, ATTR_ORDER, ATTR_SHORT, CATEGORY_ORDER, POS_LABEL, initials } from "../data/labels";

interface Props {
  nickname: string;
  country: string;
  position: Pos;
  potential: number;
  role: string;
  attrs: number[]; // ordem ATTR_ORDER
  club?: string;
  age?: number;
}

export function StickerCard({ nickname, country, position, potential, role, attrs, club, age }: Props) {
  return (
    <div className="sticker">
      <div className="corner-tape tl" />
      <div className="corner-tape br" />
      <div className="sticker-inner">
        <div className="sticker-ovr">
          {potential}
          <span className="tiny">Potencial</span>
        </div>
        <div className="sticker-pos">
          {POS_LABEL[position]} · {country}
        </div>
        <div className="sticker-photo">{initials(nickname)}</div>
        <div className="sticker-name">{nickname}</div>
        {club && <div className="sticker-club">{club}</div>}
        <div className="sticker-trait">{role}</div>
        {CATEGORY_ORDER.map((cat) => (
          <div className="attr-cat" key={cat}>
            <div className="attr-cat-title">{cat}</div>
            <div className="attr-cat-grid">
              {ATTR_ORDER.filter((a) => ATTR_CATEGORY[a] === cat).map((a) => (
                <div className="sattr" key={a}>
                  <span>{ATTR_SHORT[a]}</span>
                  <b>{attrs[ATTR_ORDER.indexOf(a)]}</b>
                </div>
              ))}
            </div>
          </div>
        ))}
        <div className="sticker-foot">{age ? `${age} anos · ` : ""}Várzea Lendas</div>
      </div>
    </div>
  );
}
