// Build-time render of production meshes, matching vanilla's pre-rendered model sprites.
// No camera, RenderTexture or software rasterization runs in the shipped mod.
const fs=require('fs'),path=require('path'),sharp=require('sharp');
const meshes=path.resolve(__dirname,'../artifacts/previews/inventory-meshes');
const assets=path.resolve(__dirname,'../DVSurvival.Game/ItemAssets');
const names=['Meal','Water','Coffee','FirstAid','HeatPack','CannedFood','Tea'];
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0),sub=(a,b)=>a.map((x,i)=>x-b[i]);
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const edge=(a,b,x,y)=>(b[0]-a[0])*(y-a[1])-(b[1]-a[1])*(x-a[0]);
const right=[.94,0,.342],up=[-.1,.956,.275],view=[.327,.293,-.899];
const unit=v=>{const length=Math.sqrt(dot(v,v));return v.map(x=>x/length);};
const lightDirection=unit([-.35,.75,-.56]),halfDirection=unit(lightDirection.map((v,i)=>v+view[i]));
async function render(language) {
  const {data:atlas,info}=await sharp(path.join(assets,language==='ru'?'provisions.png':'provisions-en.png')).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  const materials=await sharp(path.join(assets,'provisions-materials.png')).ensureAlpha().raw().toBuffer();
  const layers=[];
  for(let k=0;k<names.length;k++) {
    const model=JSON.parse(fs.readFileSync(path.join(meshes,names[k]+'.json')));
    const W=768,H=768,buffer=Buffer.alloc(W*H*4),depth=new Float32Array(W*H).fill(-Infinity);
    const q=model.vertices.map(p=>[dot(p,right),-dot(p,up),dot(p,view)]);
    const minX=Math.min(...q.map(p=>p[0])),maxX=Math.max(...q.map(p=>p[0]));
    const minY=Math.min(...q.map(p=>p[1])),maxY=Math.max(...q.map(p=>p[1]));
    const scale=.84*W/Math.max(maxX-minX,maxY-minY);
    const projected=q.map(p=>[(p[0]-(minX+maxX)/2)*scale+W/2,(p[1]-(minY+maxY)/2)*scale+H/2,p[2]]);
    for(let i=0;i<model.triangles.length;i+=3) {
      const ix=model.triangles.slice(i,i+3),v=ix.map(j=>model.vertices[j]),p=ix.map(j=>projected[j]),uv=ix.map(j=>model.uv[j]);
      const normal=cross(sub(v[1],v[0]),sub(v[2],v[0])),normals=ix.map(j=>model.normals[j]);
      if(dot(normal,view)<=1e-10)continue;
      const area=edge(p[0],p[1],p[2][0],p[2][1]); if(area<=0)continue;
      const x0=Math.max(0,Math.floor(Math.min(...p.map(p=>p[0])))),x1=Math.min(W-1,Math.ceil(Math.max(...p.map(p=>p[0]))));
      const y0=Math.max(0,Math.floor(Math.min(...p.map(p=>p[1])))),y1=Math.min(H-1,Math.ceil(Math.max(...p.map(p=>p[1]))));
      for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++) {
        const a=edge(p[1],p[2],x+.5,y+.5)/area,b=edge(p[2],p[0],x+.5,y+.5)/area,c=1-a-b;
        if(a<0||b<0||c<0)continue;
        const z=a*p[0][2]+b*p[1][2]+c*p[2][2],pixel=y*W+x; if(z<depth[pixel])continue;
        depth[pixel]=z;
        const u=a*uv[0][0]+b*uv[1][0]+c*uv[2][0],vt=a*uv[0][1]+b*uv[1][1]+c*uv[2][1];
        const tx=Math.min(info.width-1,Math.max(0,Math.round(u*info.width-.5))),ty=Math.min(info.height-1,Math.max(0,Math.round((1-vt)*info.height-.5)));
        const n=unit([0,1,2].map(ch=>a*normals[0][ch]+b*normals[1][ch]+c*normals[2][ch]));
        const texel=(ty*info.width+tx)*4,metal=materials[texel]/255,gloss=materials[texel+3]/255;
        const diffuse=.45+.55*Math.max(0,dot(n,lightDirection));
        const reflectedY=2*dot(n,view)*n[1]-view[1];
        const reflection=.37+.28*(reflectedY*.5+.5);
        const specular=Math.pow(Math.max(0,dot(n,halfDirection)),12+gloss*90)*(.06+gloss*.3);
        for(let ch=0;ch<3;ch++) {
          const albedo=atlas[texel+ch]/255;
          const color=albedo*diffuse*(1-metal*.65)+albedo*reflection*metal+specular;
          buffer[pixel*4+ch]=Math.max(0,Math.min(255,Math.round(color*255)));
        }
        buffer[pixel*4+3]=255;
      }
    }
    if(names[k]==='FirstAid' && language==='ru') {
      await sharp(buffer,{raw:{width:W,height:H,channels:4}}).flatten({background:'#25352a'}).png()
        .toFile(path.resolve(__dirname,'../artifacts/previews/firstaid-case-ru.png'));
    }
    const standard=await sharp(buffer,{raw:{width:W,height:H,channels:4}}).resize(256,256).png().toBuffer();
    const ghost=await sharp(standard).grayscale().png().toBuffer();
    layers.push({input:standard,left:k*256,top:0},{input:ghost,left:k*256,top:256});
  }
  const output=path.join(assets,'inventory-'+language+'.png');
  await sharp({create:{width:names.length*256,height:512,channels:4,background:{r:0,g:0,b:0,alpha:0}}}).composite(layers).png().toFile(output);
  const {data,info:result}=await sharp(output).raw().toBuffer({resolveWithObject:true});
  for(let k=0;k<names.length;k++) {
    let opaque=0;
    for(let y=0;y<256;y++)for(let x=k*256;x<(k+1)*256;x++) if(data[(y*result.width+x)*result.channels+3]>128)opaque++;
    if(opaque<8000||opaque>54000)throw Error('Invalid inventory framing: '+names[k]);
  }
  console.log('Verified '+output);
}
async function preview() {
  const titles=['Паёк','Вода','Термос с кофе','Аптечка','Грелка','Консервы','Термос с чаем'];
  const heights=names.map(name=>{
    const mesh=JSON.parse(fs.readFileSync(path.join(meshes,name+'.json')));
    return Math.round(Math.max(...mesh.vertices.map(p=>p[1]))*100);
  });
  let background='<svg xmlns="http://www.w3.org/2000/svg" width="1240" height="740"><rect width="1240" height="740" fill="#17251f"/><text x="28" y="40" fill="#e5dcc4" font-family="Arial" font-size="23">Дорожный запас · модели для дальних поездок</text><text x="28" y="65" fill="#a8b6aa" font-family="Arial" font-size="13">Предпросмотр по геометрии мода · одинаковый размер карточек, высота предмета указана отдельно</text>';
  const layers=[];
  for(let i=0;i<names.length;i++) {
    const x=20+(i%4)*304,y=85+Math.floor(i/4)*318;
    background+=`<rect x="${x}" y="${y}" width="292" height="301" rx="8" fill="#25352a" stroke="#6c745a"/><text x="${x+146}" y="${y+271}" fill="#e5dcc4" font-family="Arial" font-size="17" text-anchor="middle">${titles[i]}</text><text x="${x+146}" y="${y+290}" fill="#a8b6aa" font-family="Arial" font-size="13" text-anchor="middle">Высота: ${heights[i]} см</text>`;
    const sprite=await sharp(path.join(assets,'inventory-ru.png')).extract({left:i*256,top:0,width:256,height:256}).png().toBuffer();
    layers.push({input:sprite,left:x+18,top:y+4});
  }
  background+='</svg>';
  const output=path.resolve(__dirname,'../artifacts/previews/travel-items-realistic-ru.png');
  await sharp(Buffer.from(background)).composite(layers).png().toFile(output);
  console.log('Travel model preview: '+output);
}
Promise.all([render('ru'),render('en')]).then(preview).catch(e=>{console.error(e);process.exitCode=1;});
