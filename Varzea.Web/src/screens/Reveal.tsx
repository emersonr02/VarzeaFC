import type { Pos } from "../api/types";
import { StickerCard } from "../components/StickerCard";

interface Props {
  nickname: string;
  country: string;
  position: Pos;
  potential: number;
  role: string;
  attrs: number[];
  onContinue: () => void;
  onRestart: () => void;
}

export function Reveal({ nickname, country, position, potential, role, attrs, onContinue, onRestart }: Props) {
  return (
    <section className="screen pitch-bg" style={{ textAlign: "center" }}>
      <div className="wrap">
        <span className="badge-eyebrow">Figurinha completa</span>
        <h2 className="h2" style={{ textAlign: "center" }}>Seu craque está <span className="accent">pronto</span></h2>
        <div className="sticker-stage">
          <StickerCard nickname={nickname} country={country} position={position} potential={potential} role={role} attrs={attrs} age={16} />
        </div>
        <button className="btn rust" onClick={onContinue}>Escolher modo de carreira ⚽</button>
        <br /><br />
        <button className="btn secondary" onClick={onRestart}>Refazer tudo</button>
      </div>
    </section>
  );
}
