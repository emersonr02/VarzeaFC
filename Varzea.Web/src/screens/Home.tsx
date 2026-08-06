interface Props {
  onPlay: () => void;
  onAlbum: () => void;
}

export function Home({ onPlay, onAlbum }: Props) {
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <span className="badge-eyebrow">⚽ Fantasia de futebol · grátis · sem cadastro</span>
        <h1 className="title-hero">
          VÁRZEA<br /><span className="accent">LENDAS</span>
        </h1>
        <p className="subtitle">
          Roube atributos de lendas, descubra seu potencial em cada posição e viva a carreira
          inteira: liga, copas, seleção, transferências e lesões.
        </p>

        <div className="mode-grid">
          <div className="mode-card" onClick={onPlay}>
            <span className="mode-tag">Modo Jogador</span>
            <div className="mode-title">Draft &amp; Carreira</div>
            <div className="mode-desc">Nome, nacionalidade, draft de 8 atributos, escolha sua posição e viva a carreira inteira.</div>
          </div>
          <div className="mode-card disabled" title="Em breve">
            <span className="soon-pill">Em breve</span>
            <span className="mode-tag">Modo Técnico</span>
            <div className="mode-title">Prancheta &amp; Banco</div>
            <div className="mode-desc">Crie seu treinador, escolha um estilo tático e construa seu legado na beira do campo.</div>
          </div>
        </div>

        <div className="foot-links">
          <a onClick={onAlbum}>📖 Meu álbum de figurinhas</a>
        </div>

        <p className="small-note">
          O motor de simulação roda no servidor — o resultado da sua carreira é sempre
          recalculado lá, nunca inventado no navegador.
        </p>
      </div>
    </section>
  );
}
