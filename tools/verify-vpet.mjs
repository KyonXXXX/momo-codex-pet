import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const catalog=JSON.parse(await readFile('assets/vpet-catalog.json','utf8'));
if(catalog.commit!=='2e99a42ebeff71d792118f2e8de744b773042f8d'||catalog.clips.length!==609||catalog.clips.reduce((n,c)=>n+c.frames.length,0)!==6181)throw Error('Unexpected asset coverage');
const ids=new Set(catalog.clips.map(c=>c.id));
for(const r of catalog.recipes)if(!ids.has(r.back)||r.front&&!ids.has(r.front))throw Error('Missing recipe layer');
for(const f of catalog.blobs){
 if(!/^vpet-full\/[0-9a-f]{40}\.png$/.test(f.file))throw Error('Invalid asset path');
 const b=await readFile('assets/'+f.file);
 if(b.length!==f.size||createHash('sha1').update(`blob ${b.length}\0`).update(b).digest('hex')!==f.gitBlob)throw Error('Asset mismatch: '+f.file);
}
console.log(JSON.stringify({clips:catalog.clips.length,frames:6181,verifiedFiles:catalog.blobs.length,recipes:catalog.recipes.length,foods:catalog.foods.length,activities:catalog.activities.length,result:'PASS'}));
