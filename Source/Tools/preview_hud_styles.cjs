// Render recorded production layout commands offline. Fonts/Unity blending may differ in-game.
const fs=require('fs'),path=require('path'),sharp=require('sharp');
const dir=path.resolve(__dirname,'../artifacts/previews/hud-styles');
const assets=path.resolve(__dirname,'../DVSurvival.Game/Assets');
const suffix=process.argv.includes('--alert')?'-alert':'';
const cache=new Map();
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
const rgb=c=>`rgb(${Math.round(c.r*255)},${Math.round(c.g*255)},${Math.round(c.b*255)})`;
async function texture(name,crop,color) {
  const key=JSON.stringify([name,crop,color]);if(cache.has(key))return cache.get(key);
  let s=sharp(path.join(assets,name+'.png'));if(crop)s=s.extract(crop);
  let data;
  if(color){const r=await s.ensureAlpha().raw().toBuffer({resolveWithObject:true});
    for(let i=0;i<r.data.length;i+=4){r.data[i]*=color.r;r.data[i+1]*=color.g;r.data[i+2]*=color.b;r.data[i+3]*=color.a;}
    data=await sharp(r.data,{raw:r.info}).png().toBuffer();
  }else data=await s.png().toBuffer();
  const uri='data:image/png;base64,'+data.toString('base64');cache.set(key,uri);return uri;
}
const img=(uri,r)=>`<image x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}" href="${uri}" preserveAspectRatio="none"/>`;
const frame=v=>({left:(v%11)*96,top:Math.floor(v/11)*96,width:96,height:96});
async function render(c,i){const r=c.rect;
  switch(c.kind){
    case 'solid':return `<rect x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}" fill="${rgb(c.color)}" opacity="${c.color.a}"/>`;
    case 'text':{const center=c.style.align==='center',colored=/^<color=(#[0-9A-Fa-f]{6})>(.*)<\/color>$/.exec(c.text);return `<clipPath id="t${i}"><rect x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}"/></clipPath><text clip-path="url(#t${i})" x="${r.x+(center?r.width/2:0)}" y="${r.y+r.height/2+c.style.size*.34}" font-family="${c.style.font}" font-size="${c.style.size}" font-weight="${c.style.bold?'bold':'normal'}" text-anchor="${center?'middle':'start'}" fill="${colored?colored[1]:'#edf5ff'}">${esc(colored?colored[2]:c.text)}</text>`;}
    case 'icon':return img(await texture('icons',{left:c.index*64,top:0,width:64,height:64},c.color),r);
    case 'thermal':return img(await texture('thermal'),r);
    case 'ring':case 'dial':return img(await texture(c.kind==='dial'?'dial':'rings',c.kind==='dial'?null:frame(100),c.kind==='dial'?null:{r:.16,g:.2,b:.25,a:1}),r)+img(await texture('rings',frame(Math.round(c.value*100)),c.color),r);
    case 'plate':{
      let out='';const xs=[0,16,112,128], ys=xs;
      const inset=16*(c.scale||1);
      const dx=[r.x,r.x+inset,r.x+r.width-inset,r.x+r.width],dy=[r.y,r.y+inset,r.y+r.height-inset,r.y+r.height];
      for(let y=0;y<3;y++)for(let x=0;x<3;x++)out+=img(await texture('panel',{left:xs[x],top:ys[y],width:xs[x+1]-xs[x],height:ys[y+1]-ys[y]}),{x:dx[x],y:dy[y],width:dx[x+1]-dx[x],height:dy[y+1]-dy[y]});return out;
    }
  }return '';
}
(async()=>{
 for(const lang of ['ru','en']){
  const names=lang==='ru'?['Кольца','Полосы','Строка','Минимум','Латунь','Классический']:['Rings','Bars','Ribbon','Minimal','Brass','Classic'];
  let svg='<svg xmlns="http://www.w3.org/2000/svg" width="860" height="870"><defs><linearGradient id="bg" x2="0" y2="1"><stop stop-color="#233440"/><stop offset="1" stop-color="#111e28"/></linearGradient></defs><rect width="860" height="870" fill="url(#bg)"/>';
  svg+=`<text x="28" y="36" fill="#f1f4f8" font-family="Arial" font-size="23">DVSurvival · ${lang==='ru'?'6 стилей / 100% / ширина 338 px':'6 styles / 100% / width 338 px'}</text><text x="28" y="64" fill="#abb9c7" font-family="Arial" font-size="13">${lang==='ru'?'Офлайн-превью по геометрии HUD · не скриншот игры':'Offline production HUD geometry · not an in-game screenshot'}</text>`;
  for(let k=0;k<6;k++){
    const commands=JSON.parse(fs.readFileSync(path.join(dir,`style-${k}-${lang}${suffix}.json`),'utf8'));
    const needs=commands.filter(c=>c.panel===0),root=needs[0].rect;
    const x=20+(k%2)*420,y=94+Math.floor(k/2)*220;
    svg+=`<rect x="${x}" y="${y}" width="400" height="210" rx="8" fill="#182630"/><text x="${x+12}" y="${y+24}" font-family="Arial" font-size="17" fill="#d4dee8">${names[k]}</text><g transform="translate(${x+20-root.x},${y+38-root.y})">`;
    for(let i=0;i<needs.length;i++)svg+=await render(needs[i],`${k}-${i}`);svg+='</g>';
    let scene=`<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="#233440"/><text x="28" y="44" font-family="Arial" font-size="22" fill="white">${names[k]} · offline preview · 100%</text>`;
    for(let i=0;i<commands.length;i++)scene+=await render(commands[i],`scene-${k}-${i}`);
    scene+='</svg>';
    await sharp(Buffer.from(scene)).png().toFile(path.join(dir,`style-${k}-${lang}${suffix}-full.png`));
    if(k===5)await sharp(Buffer.from(scene)).png().toFile(path.join(dir,`classic-${lang}${suffix}.png`));
  }
  svg+=`<text x="28" y="775" fill="#abb9c7" font-family="Arial" font-size="14">${lang==='ru'?'Воздух и температура тела — отдельные перемещаемые блоки':'Air and body temperature are independently movable blocks'}</text>`;
  const examples=JSON.parse(fs.readFileSync(path.join(dir,`style-4-${lang}${suffix}.json`),'utf8'));
  for(const panel of [1,2]){
    const part=examples.filter(c=>c.panel===panel),root=part[0].rect;
    svg+=`<g transform="translate(${(panel===1?28:448)-root.x},${796-root.y})">`;
    for(let i=0;i<part.length;i++)svg+=await render(part[i],`example-${panel}-${i}`);svg+='</g>';
  }
  svg+='</svg>';await sharp(Buffer.from(svg)).png().toFile(path.join(dir,`overview-${lang}${suffix}.png`));
 }
 console.log('Rendered both HUD layout overview images.');
})().catch(e=>{console.error(e);process.exit(1)});
