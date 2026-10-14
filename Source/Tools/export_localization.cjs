// Export literal Text(Russian, English) pairs as a Language Helper / I2 CSV.
const fs = require('fs'), path = require('path');
const root = path.resolve(__dirname, '../DVSurvival.Game');
const entries = new Map();
function key(s) { let h = 2166136261; for(let i=0;i<s.length;i++) h=Math.imul(h^s.charCodeAt(i),16777619)>>>0; return 'DVSurvival/'+h.toString(16).padStart(8,'0'); }
const literal = '"(?:[^"\\\\]|\\\\.)*"';
const regex = new RegExp('\\bText\\(\\s*('+literal+')\\s*,\\s*('+literal+')\\s*\\)', 'g');
function sources(directory) {
  return fs.readdirSync(directory,{withFileTypes:true}).flatMap(entry => {
    const file=path.join(directory,entry.name);
    if(entry.isDirectory()) return ['bin','obj','artifacts'].includes(entry.name) ? [] : sources(file);
    return entry.name.endsWith('.cs') ? [file] : [];
  });
}
for (const file of sources(root)) {
  for (const m of fs.readFileSync(file,'utf8').matchAll(regex)) {
    const ru=JSON.parse(m[1]),en=JSON.parse(m[2]),id=key(en), previous=entries.get(id);
    if(previous && previous.en!==en) throw Error('Translation hash collision: '+en);
    // Context-neutral English labels may legitimately share a Russian synonym.
    if(!previous) entries.set(id,{en,ru});
  }
}
if(entries.size<70) throw Error('Translation extraction unexpectedly incomplete: '+entries.size);
const quote=s=>'"'+s.replaceAll('"','""')+'"';
const rows=['Key,Type,Desc,English,Russian', ...[...entries].sort().map(([id,{en,ru}])=>[id,'Text','',en,ru].map(quote).join(','))];
fs.mkdirSync(path.join(root,'Localization'),{recursive:true});
fs.writeFileSync(path.join(root,'Localization/strings.csv'),'\uFEFF'+rows.join('\r\n')+'\r\n','utf8');
console.log('Language Helper catalogue: '+entries.size+' RU/EN terms.');
