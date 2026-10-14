// Build-time printed travel packaging. One opaque atlas/material is reused by all seven models.
const fs = require('fs'), path = require('path'), sharp = require('sharp');
const palette = ['#c4b38b','#3d4c3a','#397e8e','#ddd8bf','#555c54','#afb5ac','#6c4931','#b54135','#c58948','#292d2a'];
const metallic = [0,0,0,0,.75,.9,0,0,.12,0];
const smoothness = [.22,.12,.38,.32,.36,.48,.2,.28,.23,.16];
const noise=(x,y,seed)=>{let n=Math.imul(x+1,73856093)^Math.imul(y+1,19349663)^Math.imul(seed+1,83492791);n=Math.imul(n^(n>>>13),1274126177);return ((n^(n>>>16))>>>0)/4294967295;};
function bodyTiles() {
  return palette.map((hex,index)=>{
    const rgb=[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16)),data=Buffer.alloc(128*128*3);
    for(let y=0;y<128;y++) for(let x=0;x<128;x++) {
      let texture=(noise(x,y,index)-.5)*4;
      if([1,9].includes(index)) texture+=(x%4===0?-1.5:.3)+(y%4===0?-1:.2);
      if([4,5].includes(index)) texture+=(noise(0,y,index)-.5)*10+(noise(x,y,index)-.5)*3;
      if(index===6) texture+=(noise(x>>1,y>>1,index)-.5)*5;
      if([0,8].includes(index)) texture+=Math.sin(y*.14+x*.04)*1.5;
      for(let c=0;c<3;c++) data[(y*128+x)*3+c]=Math.max(0,Math.min(255,Math.round(rgb[c]+texture)));
    }
    return {input:data,raw:{width:128,height:128,channels:3},left:index*128,top:320};
  });
}
const bowl='<path d="M98 111h60l-7 23h-46zm-3-7h66v6H95zm15-17h5v12h-5zm14-4h5v16h-5zm14 4h5v12h-5z"/>';
const drop='<path d="M128 84c-8 15-23 29-23 42a23 23 0 0046 0c0-13-15-27-23-42z"/>';
const flask='<path d="M115 87h26v8h-26zm-5 12h36v44h-36zm4 5v30h28v-30z" fill-rule="evenodd"/>';
const cross='<path d="M118 88h20v19h19v20h-19v19h-20v-19H99v-20h19z"/>';
const heat='<path d="M110 143c-22-22 13-27 0-52m19 52c-22-22 13-27 0-52m19 52c-22-22 13-27 0-52" fill="none" stroke="currentColor" stroke-width="7"/>';
const can='<path d="M104 98h48v44h-48zm0-9h48v6h-48zm0 56h48v5h-48zm14-29h20v10h-20z" fill-rule="evenodd"/>';
const leaf='<path d="M103 132c0-32 29-35 53-36-1 31-22 53-44 43l28-29-5-4z"/>';
const labels = [
  ['#d7c99e','#394833','ПАЁК','ПОЛЕВОЙ РАЦИОН','ДЛЯ ДАЛЬНЕГО РЕЙСА','450 г',bowl],
  ['#e1e1cb','#276375','ВОДА','ПИТЬЕВАЯ','ДОРОЖНАЯ БУТЫЛКА','500 мл',drop],
  ['#dec9a5','#583c28','КОФЕ','ВАКУУМНЫЙ ТЕРМОС','ПЛОТНО ЗАКРОЙТЕ КРЫШКУ','350 мл',flask],
  ['#dfd7ba','#973e33','АПТЕЧКА','ДОРОЖНЫЙ НАБОР','БЕРЕЧЬ ОТ ВЛАГИ','1 набор',cross],
  ['#e4c79a','#99552d','ГРЕЛКА','КАРМАННЫЙ ПАКЕТ','СУХОЕ ТЕПЛО','1 пакет',heat],
  ['#dbceb0','#754238','КОНСЕРВЫ','ТУШЁНОЕ МЯСО','ГОТОВО К УПОТРЕБЛЕНИЮ','400 г',can],
  ['#d4d0b0','#3b5038','ЧАЙ','ТРАВЯНОЙ • В ТЕРМОСЕ','ДЛЯ ДАЛЬНЕГО РЕЙСА','350 мл',leaf]
];
const english = [
 ['RATION','FIELD RATION','FOR THE LONG HAUL','450 g'],
 ['WATER','DRINKING WATER','TRAVEL BOTTLE','500 ml'],
 ['COFFEE','VACUUM FLASK','KEEP THE LID CLOSED','350 ml'],
 ['FIRST AID','TRAVEL MEDICAL KIT','KEEP DRY','1 kit'],
 ['HEAT PACK','POCKET WARMER','DRY HEAT','1 pack'],
 ['CANNED FOOD','STEWED MEAT','READY TO EAT','400 g'],
 ['TEA','HERBAL / VACUUM FLASK','FOR THE LONG HAUL','350 ml']
];
async function generate(russian) {
  let svg = '<svg xmlns="http://www.w3.org/2000/svg" width="2048" height="512"><rect width="2048" height="512" fill="#292d2a"/>';
  labels.forEach(([paper,ink,title,sub,note,amount,symbol],i) => {
    if (!russian) [title,sub,note,amount] = english[i];
    if (i===3) {
      // Screen-printed white cross on moulded red plastic, not a paper/fabric label.
      svg+=`<g transform="translate(${i*256} 0)" fill="#f0efdf" font-family="Arial, sans-serif" text-anchor="middle"><rect width="256" height="256" fill="${palette[7]}"/><path d="M105 28h46v51h51v46h-51v51h-46v-51H54V79h51z"/><text x="128" y="216" font-size="23" font-weight="bold" letter-spacing="1">${title}</text><text x="128" y="238" font-size="10">${sub}</text></g>`;
      return;
    }
    svg += `<g transform="translate(${i*256} 0)" fill="${ink}" color="${ink}" font-family="Arial, sans-serif" text-anchor="middle"><rect width="256" height="256" fill="${paper}"/><rect x="14" y="14" width="228" height="228" rx="4" fill="none" stroke="${ink}" stroke-width="2"/><path d="M24 40h208M24 199h208" stroke="${ink}" opacity=".75"/><text x="128" y="32" font-size="11" letter-spacing="2">${russian ? 'ДОРОЖНЫЙ ЗАПАС' : 'TRAVEL SUPPLIES'}</text><text x="128" y="72" font-size="${title.length>9?21:27}" font-weight="bold" letter-spacing="1">${title}</text>${symbol}<text x="128" y="173" font-size="12" font-weight="bold">${sub}</text><text x="128" y="189" font-size="9">${note}</text><text x="54" y="225" font-size="13" font-weight="bold">${amount}</text>`;
    // Small deterministic wear marks belong to the printed texture, never to runtime decals.
    for (let mark=0;mark<22;mark++) {
      const x=20+(mark*43+i*11)%215,y=20+(mark*67+i*7)%212;
      svg+=`<path d="M${x} ${y}h${2+mark%5}" stroke="${ink}" opacity=".09"/>`;
    }
    for (let b=0;b<35;b++) if ((b*7+3)%5<3) svg += `<rect x="${140+b*2.4}" y="208" width="${b%3===0?2:1}" height="20"/>`;
    svg += '</g>';
  });
  palette.forEach((color,i)=>svg+=`<rect x="${i*128}" y="320" width="128" height="128" fill="${color}"/>`);
  svg += '</svg>';
  const out=path.join(__dirname,'../DVSurvival.Game/ItemAssets');
  fs.mkdirSync(out,{recursive:true});
  await sharp(Buffer.from(svg)).composite(bodyTiles()).png().toFile(path.join(out,russian ? 'provisions.png' : 'provisions-en.png'));
}
async function generateMaterials() {
  const width=2048,height=512,data=Buffer.alloc(width*height*4);
  for(let y=0;y<height;y++) for(let x=0;x<width;x++) {
    const pixel=(y*width+x)*4,index=Math.floor(x/128);
    const surface=y>=320&&y<448&&index<palette.length;
    data[pixel]=surface?Math.round(metallic[index]*255):0;
    data[pixel+3]=surface?Math.round(smoothness[index]*255+(noise(x,y,17)-.5)*5):70;
  }
  await sharp(data,{raw:{width,height,channels:4}}).png().toFile(path.join(__dirname,'../DVSurvival.Game/ItemAssets/provisions-materials.png'));
}
Promise.all([generate(true),generate(false),generateMaterials()]).then(()=>console.log('Generated textured RU/EN travel labels and shared material map.')).catch(e=>{console.error(e);process.exitCode=1;});
